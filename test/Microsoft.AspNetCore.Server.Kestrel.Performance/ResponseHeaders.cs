// Copyright (c) .NET Foundation. All rights reserved.
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
                _response.ContentType = "text/plain";
                _response.ContentLength = 13;

                var headers = _response.Headers;

                headers["Connection"] = "Close";
                headers["Cache-Control"] = "Test Value";
                headers["Keep-Alive"] = "Test Value";
                headers["Pragma"] = "Test Value";
                headers["Trailer"] = "Test Value";
                headers["Transfer-Encoding"] = "Test Value";
                headers["Upgrade"] = "Test Value";
                headers["Via"] = "Test Value";
                headers["Warning"] = "Test Value";
                headers["Allow"] = "Test Value";
                headers["Content-Encoding"] = "Test Value";
                headers["Content-Language"] = "Test Value";
                headers["Content-Location"] = "Test Value";
                headers["Content-MD5"] = "Test Value";
                headers["Content-Range"] = "Test Value";
                headers["Expires"] = "Test Value";
                headers["Last-Modified"] = "Test Value";

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

                headers["Unknown"] = "Unknown";
                headers["IUnknown"] = "Unknown";
                headers["X-Unknown"] = "Unknown";

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
