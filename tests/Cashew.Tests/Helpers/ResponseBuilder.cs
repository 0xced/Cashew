﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Cashew.Headers;

namespace Cashew.Tests.Helpers
{
    public class ResponseBuilder
    {
        private static ResponseBuilder _instance;

        private readonly List<Action<HttpResponseMessage>> _builderActions = new List<Action<HttpResponseMessage>>();

        public static ResponseBuilder Response(HttpStatusCode statusCode)
        {
            var instance = _instance ?? (_instance = new ResponseBuilder());
            instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.StatusCode = statusCode;
            });

            return instance;
        }

        public ResponseBuilder Created(DateTimeOffset date)
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Headers.Date = date;
            });
           
            return _instance;
        }

        public ResponseBuilder WithSharedMaxAge(int ageInSeconds)
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Headers.CacheControl.SharedMaxAge = TimeSpan.FromSeconds(ageInSeconds);
            });

            return _instance;
        }

        public ResponseBuilder WithMaxAge(int ageInSeconds)
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Headers.CacheControl.MaxAge = TimeSpan.FromSeconds(ageInSeconds);
            });

            return _instance;
        }

        public ResponseBuilder Expires(DateTimeOffset date)
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Content.Headers.Expires = date;
            });
            
            return _instance;
        }

        public ResponseBuilder WithNoCache()
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Headers.CacheControl.NoCache = true;
            });

            return _instance;
        }

        public ResponseBuilder WithNoStore()
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Headers.CacheControl.NoStore = true;
            });

            return _instance;
        }

        public ResponseBuilder WithMustRevalidate()
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Headers.CacheControl.MustRevalidate = true;
            });

            return _instance;
        }

        public ResponseBuilder WithProxyRevalidate()
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Headers.CacheControl.ProxyRevalidate = true;
            });

            return _instance;
        }

        public ResponseBuilder WithETag(string tag)
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Headers.ETag = new EntityTagHeaderValue(tag);
            });

            return _instance;
        }

        public ResponseBuilder LastModified(DateTimeOffset lastModified)
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Content.Headers.LastModified = lastModified;
            });

            return _instance;
        }

        public ResponseBuilder WithStatusHeader(CacheStatus status)
        {
            _instance._builderActions.Add(delegate (HttpResponseMessage response)
            {
                EnsureCacheControlHeaders(response);
                response.Headers.AddClientCacheStatusHeader(status);
            });

            return _instance;
        }

        private static void EnsureCacheControlHeaders(HttpResponseMessage response)
        {
            if (response.Headers.CacheControl == null)
            {
                response.Headers.CacheControl = new CacheControlHeaderValue();
            }
        }


        public HttpResponseMessage Build()
        {
            var response = new HttpResponseMessage { Content = new ByteArrayContent(new byte[256]) };

            _builderActions.ForEach(a => a(response));
            _builderActions.Clear();

            return response;
        }
    }
}