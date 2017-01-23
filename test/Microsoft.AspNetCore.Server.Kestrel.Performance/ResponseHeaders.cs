﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class ResponseHeaders
    {
        private const int InnerLoopCount = 512;

        private static readonly byte[] _bytesServer = Encoding.ASCII.GetBytes("\r\nServer: Kestrel");
        private static readonly DateHeaderValueManager _dateHeaderValueManager = new DateHeaderValueManager();
        private static readonly MemoryPool _memoryPool = new MemoryPool();
        private FrameResponseHeaders _responseHeadersDirect;
        private HttpResponse _response;

        [Params("ContentLengthNumeric", "ContentLengthString", "Plaintext", "Common", "Unknown")]
        public string Type { get; set; }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void SetHeaders()
        {
            switch (Type)
            {
                case "ContentLengthNumeric":
                    ContentLengthNumeric(InnerLoopCount);
                    break;
                case "ContentLengthString":
                    ContentLengthString(InnerLoopCount);
                    break;
                case "Plaintext":
                    Plaintext(InnerLoopCount);
                    break;
                case "Common":
                    Common(InnerLoopCount);
                    break;
                case "Unknown":
                    Unknown(InnerLoopCount);
                    break;
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void OutputHeaders()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                var block = _memoryPool.Lease();
                var iter = new MemoryPoolIterator(block);
                _responseHeadersDirect.CopyTo(ref iter);

                ReturnBlocks(block);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ContentLengthNumeric(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _responseHeadersDirect.Reset();

                _response.ContentLength = 0;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ContentLengthString(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _responseHeadersDirect.Reset();

                _response.Headers["Content-Length"] = "0";
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Plaintext(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _responseHeadersDirect.Reset();

                _response.StatusCode = 200;
                _response.ContentType = "text/plain";
                _response.ContentLength = 13;

                var dateHeaderValues = _dateHeaderValueManager.GetDateHeaderValues();
                _responseHeadersDirect.SetRawDate(dateHeaderValues.String, dateHeaderValues.Bytes);
                _responseHeadersDirect.SetRawServer("Kestrel", _bytesServer);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Common(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _responseHeadersDirect.Reset();

                _response.StatusCode = 200;
                _response.ContentType = "text/css";
                _response.ContentLength = 421;

                var headers = _response.Headers;

                headers["Connection"] = "Close";
                headers["Cache-Control"] = "public, max-age=30672000";
                headers["Vary"] = "Accept-Encoding";
                headers["Content-Encoding"] = "gzip";
                headers["Expires"] = "Fri, 12 Jan 2018 22:01:55 GMT";
                headers["Last-Modified"] = "Wed, 22 Jun 2016 20:08:29 GMT";
                headers["Set-Cookie"] = "prov=20629ccd-8b0f-e8ef-2935-cd26609fc0bc; __qca=P0-1591065732-1479167353442; _ga=GA1.2.1298898376.1479167354; _gat=1; sgt=id=9519gfde_3347_4762_8762_df51458c8ec2; acct=t=why-is-%e0%a5%a7%e0%a5%a8%e0%a5%a9-numeric&s=why-is-%e0%a5%a7%e0%a5%a8%e0%a5%a9-numeric";
                headers["ETag"] = "\"54ef7954-1078\"";
                headers["Transfer-Encoding"] = "chunked";
                headers["Content-Language"] = "en-gb";
                headers["Upgrade"] = "websocket";
                headers["Via"] = "1.1 varnish";
                headers["Access-Control-Allow-Origin"] = "*";
                headers["Access-Control-Allow-credentials"] = "true";
                headers["Access-Control-Expose-Headers"] = "Client-Protocol, Content-Length, Content-Type, X-Bandwidth-Est, X-Bandwidth-Est2, X-Bandwidth-Est-Comp, X-Bandwidth-Avg, X-Walltime-Ms, X-Sequence-Num";

                var dateHeaderValues = _dateHeaderValueManager.GetDateHeaderValues();
                _responseHeadersDirect.SetRawDate(dateHeaderValues.String, dateHeaderValues.Bytes);
                _responseHeadersDirect.SetRawServer("Kestrel", _bytesServer);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Unknown(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _responseHeadersDirect.Reset();

                _response.StatusCode = 200;
                _response.ContentType = "text/plain";
                _response.ContentLength = 13;

                var headers = _response.Headers;

                headers["Link"] = "<https://www.gravatar.com/avatar/6ae816bfaad7bbc58b17fac49ef5cced?d=404&s=250>; rel=\"canonical\"";
                headers["X-Ua-Compatible"] = "IE=Edge";
                headers["X-Powered-By"] = "ASP.NET";
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Xss-Protection"] = "1; mode=block";
                headers["X-Frame-Options"] = "SAMEORIGIN";
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
                headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self' cdnjs.cloudflare.com code.jquery.com scotthelme.disqus.com a.disquscdn.com www.google-analytics.com go.disqus.com platform.twitter.com cdn.syndication.twimg.com; style-src 'self' a.disquscdn.com fonts.googleapis.com cdnjs.cloudflare.com platform.twitter.com; img-src 'self' data: www.gravatar.com www.google-analytics.com links.services.disqus.com referrer.disqus.com a.disquscdn.com cdn.syndication.twimg.com syndication.twitter.com pbs.twimg.com platform.twitter.com abs.twimg.com; child-src fusiontables.googleusercontent.com fusiontables.google.com www.google.com disqus.com www.youtube.com syndication.twitter.com platform.twitter.com; frame-src fusiontables.googleusercontent.com fusiontables.google.com www.google.com disqus.com www.youtube.com syndication.twitter.com platform.twitter.com; connect-src 'self' links.services.disqus.com; font-src 'self' cdnjs.cloudflare.com fonts.gstatic.com fonts.googleapis.com; form-action 'self'; upgrade-insecure-requests;";

                var dateHeaderValues = _dateHeaderValueManager.GetDateHeaderValues();
                _responseHeadersDirect.SetRawDate(dateHeaderValues.String, dateHeaderValues.Bytes);
                _responseHeadersDirect.SetRawServer("Kestrel", _bytesServer);
            }
        }

        [Setup]
        public void Setup()
        {
            var connectionContext = new MockConnection(new KestrelServerOptions());
            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.Reset();
            frame.InitializeHeaders();
            _responseHeadersDirect = (FrameResponseHeaders)frame.ResponseHeaders;
            var context = new DefaultHttpContext(frame);
            _response = new DefaultHttpResponse(context);

            switch (Type)
            {
                case "ContentLengthNumeric":
                    ContentLengthNumeric(1);
                    break;
                case "ContentLengthString":
                    ContentLengthString(1);
                    break;
                case "Plaintext":
                    Plaintext(1);
                    break;
                case "Common":
                    Common(1);
                    break;
                case "Unknown":
                    Unknown(1);
                    break;
            }
        }

        private static void ReturnBlocks(MemoryPoolBlock block)
        {
            while (block != null)
            {
                var returningBlock = block;
                block = returningBlock.Next;

                returningBlock.Pool.Return(returningBlock);
            }
        }
    }
}
