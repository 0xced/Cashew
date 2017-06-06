﻿using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cashew.Core.Headers;
using Cashew.Core.Keys;
using System.Linq;

namespace Cashew.Core
{
    public class HttpCachingHandler : DelegatingHandler
    {
        private readonly IHttpCache _cache;
        private readonly ICacheKeyStrategy _keyStrategy;

        internal ISystemClock SystemClock { get; set; } = new DefaultSystemClock();

        /// <summary>
        /// The list of status codes that are seen as cacheable by <see cref="HttpCachingHandler"/>.
        /// </summary>
        public HttpStatusCode[] CacheableStatusCodes { get; set; } = {
            HttpStatusCode.OK, HttpStatusCode.NonAuthoritativeInformation, HttpStatusCode.NoContent,
            HttpStatusCode.PartialContent, HttpStatusCode.MultipleChoices, HttpStatusCode.MovedPermanently,
            HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed, HttpStatusCode.Gone,
            HttpStatusCode.RequestUriTooLong, HttpStatusCode.NotImplemented,
        };

        public HttpCachingHandler(IHttpCache cache, ICacheKeyStrategy keyStrategy)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _keyStrategy = keyStrategy ?? throw new ArgumentNullException(nameof(keyStrategy));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (!IsRequestCacheable(request))
            {
                return await base.SendAsync(request, cancellationToken);
            }

            var key = _keyStrategy.GetCacheKey(request);
            var cachedResponse = _cache.Get<HttpResponseMessage>(key);

            if (cachedResponse != null)
            {
                var currentAge = cachedResponse.CalculateAgeAt(SystemClock.UtcNow);
                var freshnessLifetime = cachedResponse.CalculateFreshnessLifetime();
                var isResponseFresh = IsResponseFresh(request, currentAge, freshnessLifetime);

                if (isResponseFresh && !cachedResponse.Headers.CacheControl.NoCache && !request.Headers.CacheControl.NoCache)
                {
                    cachedResponse.Headers.AddClientCacheStatusHeader(CacheStatus.Hit);
                    return cachedResponse;
                }

                var isStaleResponseAcceptable = IsStaleResponseAcceptable(request, cachedResponse, currentAge, freshnessLifetime);
                if (isStaleResponseAcceptable)
                {
                    cachedResponse.Headers.AddClientCacheStatusHeader(CacheStatus.Stale);
                    return cachedResponse;
                }

                //If we reach this point it must mean that the cached reponse was NOT fresh and it was NOT acceptable to return a stale response, 
                //therefore we need try to revalidate the cached response.
                cachedResponse.Headers.AddClientCacheStatusHeader(CacheStatus.Revalidated);
                request.Headers.AddCacheValidationHeader(cachedResponse);
            }
            else if (request.Headers.CacheControl.OnlyIfCached)
            {
                var gatewayResponse = new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
                {
                    RequestMessage = request,
                };
                gatewayResponse.Headers.AddClientCacheStatusHeader(CacheStatus.Miss);

                return gatewayResponse;
            }

            var serverResponse = await base.SendAsync(request, cancellationToken);

            return HandleServerResponse(request, serverResponse, key, cachedResponse);
        }

        private HttpResponseMessage HandleServerResponse(HttpRequestMessage request, HttpResponseMessage serverResponse, string cacheKey, HttpResponseMessage cachedResponse)
        {
            if (serverResponse == null)
            {
                return null;
            }

            var updatedCacheKey = _keyStrategy.GetCacheKey(request, serverResponse);

            var cashewStatusHeader = cachedResponse?.Headers.GetCashewStatusHeader();
            var wasRevalidated = cashewStatusHeader.HasValue && cashewStatusHeader.Value == CacheStatus.Revalidated;
            var isResponseCacheable = IsResponseCacheable(serverResponse);

            if (wasRevalidated)
            {
                serverResponse.Headers.AddClientCacheStatusHeader(CacheStatus.Revalidated);

                if (serverResponse.StatusCode == HttpStatusCode.NotModified)
                {
                    cachedResponse.Headers.CacheControl = serverResponse.Headers.CacheControl;
                    cachedResponse.RequestMessage = request;
                    cachedResponse.Headers.Date = SystemClock.UtcNow;

                    _cache.Put(cacheKey, cachedResponse);

                    //We need to dispose the response from the server since we're not returning it and because of that the pipeline will not dispose it.
                    serverResponse.Dispose();

                    return cachedResponse;
                }

                if (isResponseCacheable)
                {
                    _cache.Remove(cacheKey);
                    _cache.Put(updatedCacheKey, serverResponse);
                }
            }
            else if (isResponseCacheable)
            {
                _cache.Put(updatedCacheKey, serverResponse);
                serverResponse.Headers.AddClientCacheStatusHeader(CacheStatus.Miss);
            }
            else
            {
                serverResponse.Headers.AddClientCacheStatusHeader(CacheStatus.Miss);
            }
            
            return serverResponse;
        }

        private static bool IsRequestCacheable(HttpRequestMessage request)
        {
            if (request.Method != HttpMethod.Get)
            {
                return false;
            }

            var cacheControlHeaders = request.Headers.CacheControl;
            if (cacheControlHeaders != null && cacheControlHeaders.NoStore)
            {
                return false;
            }

            return true;
        }

        private static bool IsResponseFresh(HttpRequestMessage request, TimeSpan? currentAge, TimeSpan? freshnessLifeTime)
        {
            if (currentAge == null || freshnessLifeTime == null)
            {
                return false;
            }

            if (request.Headers.CacheControl.MinFresh.HasValue)
            {
                return currentAge.Value + request.Headers.CacheControl.MinFresh.Value <= freshnessLifeTime.Value;
            }

            return freshnessLifeTime.Value > currentAge.Value;
        }

        private static bool IsStaleResponseAcceptable(HttpRequestMessage request, HttpResponseMessage cachedResponse, TimeSpan? currentAge, TimeSpan? freshnessLifetime)
        {
            var responseCacheControl = cachedResponse.Headers.CacheControl;
            var requestCacheControl = request.Headers.CacheControl;
            if (responseCacheControl == null)
            {
                return false;
            }

            //todo: What do we do if request.Headers.CacheControl is null?

            if (requestCacheControl.NoCache || responseCacheControl.NoCache)
            {
                return false;
            }

            if (responseCacheControl.MustRevalidate)
            {
                return false;
            }

            if (requestCacheControl.MaxStale)
            {
                return true;
            }

            if (currentAge == null || freshnessLifetime == null)
            {
                return false;
            }

            if (requestCacheControl.MaxStaleLimit.HasValue)
            {
                var lifetimeFreshnessDifference = currentAge.Value - freshnessLifetime.Value;
                return lifetimeFreshnessDifference <= requestCacheControl.MaxStaleLimit.Value;
            }

            if (requestCacheControl.MaxAge.HasValue)
            {
                return requestCacheControl.MaxAge.Value > currentAge.Value;
            }

            return false;
        }

        private bool IsResponseCacheable(HttpResponseMessage response)
        {
            if (!CacheableStatusCodes.Contains(response.StatusCode))
            {
                return false;
            }

            var cacheControl = response.Headers.CacheControl;
            if (cacheControl == null)
            {
                return false;
            }

            if (cacheControl.NoStore)
            {
                return false;
            }

            if (response.Content == null)
            {
                return false;
            }

            return cacheControl.SharedMaxAge != null || cacheControl.MaxAge != null || response.Content.Headers.Expires != null;
        }

        protected override void Dispose(bool disposeManaged)
        {
            if (disposeManaged)
            {
                _cache.Dispose();
                _keyStrategy.Dispose();
            }

            base.Dispose(disposeManaged);
        }
    }
}