// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Performance.Mocks;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class FrameProcessingBenchmark
    {
        private const int InnerLoopCount = 512;

        private readonly HttpParser<Adapter> _parser = new HttpParser<Adapter>();

        private ReadableBuffer _buffer;

        public Frame<object> Frame { get; set; }

        [Setup]
        public void Setup()
        {
            var serviceContext = new ServiceContext()
            {
                HttpParserFactory = _ => NullParser<FrameAdapter>.Instance,
                ServerOptions = new KestrelServerOptions()
            };
            var frameContext = new FrameContext
            {
                ServiceContext = serviceContext,
                ConnectionInformation = new MockConnectionInformation
                {
                    PipeFactory = new PipeFactory()
                },
                TimeoutControl = new MockTimeoutControl()
            };

            Frame = new Frame<object>(application: null, frameContext: frameContext);
            Frame.Reset();
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = RequestParsingData.InnerLoopCount)]
        public void PlaintextTechEmpower()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerRequest);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount)]
        public void LiveAspNet()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.LiveaspnetRequest);
                ParseData();
            }
        }

        private void InsertData(byte[] data)
        {
            _buffer = ReadableBuffer.Create(data);
        }

        private void ParseData()
        {
            if (!_parser.ParseRequestLine(new Adapter(this), _buffer, out var consumed, out var examined))
            {
                ErrorUtilities.ThrowInvalidRequestHeaders();
            }

            _buffer = _buffer.Slice(consumed, _buffer.End);

            if (!_parser.ParseHeaders(new Adapter(this), _buffer, out consumed, out examined, out var consumedBytes))
            {
                ErrorUtilities.ThrowInvalidRequestHeaders();
            }

            Frame.EnsureHostHeaderExists();

            Frame.Reset();
        }

        private struct Adapter : IHttpRequestLineHandler, IHttpHeadersHandler
        {
            public FrameProcessingBenchmark RequestHandler;

            public Adapter(FrameProcessingBenchmark requestHandler)
            {
                RequestHandler = requestHandler;
            }

            public void OnHeader(Span<byte> name, Span<byte> value)
                => RequestHandler.Frame.OnHeader(name, value);

            public void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
                => RequestHandler.Frame.OnStartLine(method, version, target, path, query, customMethod, pathEncoded);
        }
    }
}
