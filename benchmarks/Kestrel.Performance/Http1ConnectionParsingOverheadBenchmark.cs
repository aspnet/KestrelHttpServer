﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO.Pipelines;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Performance.Mocks;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class Http1ConnectionParsingOverheadBenchmark
    {
        private const int InnerLoopCount = 512;

        public ReadableBuffer _buffer;
        public Http1Connection<object> _http1Connection;

        [IterationSetup]
        public void Setup()
        {
            var serviceContext = new ServiceContext
            {
                HttpParserFactory = _ => NullParser<Http1ParsingHandler>.Instance,
                ServerOptions = new KestrelServerOptions()
            };
            var http1ConnectionContext = new Http1ConnectionContext
            {
                ServiceContext = serviceContext,
                ConnectionFeatures = new FeatureCollection(),
                PipeFactory = new PipeFactory(),
                TimeoutControl = new MockTimeoutControl()
            };

            _http1Connection = new Http1Connection<object>(application: null, context: http1ConnectionContext);
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = InnerLoopCount)]
        public void Http1ConnectionOverheadTotal()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                ParseRequest();
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void Http1ConnectionOverheadRequestLine()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                ParseRequestLine();
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void Http1ConnectionOverheadRequestHeaders()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                ParseRequestHeaders();
            }
        }

        private void ParseRequest()
        {
            _http1Connection.Reset();

            if (!_http1Connection.TakeStartLine(_buffer, out var consumed, out var examined))
            {
                ErrorUtilities.ThrowInvalidRequestLine();
            }

            if (!_http1Connection.TakeMessageHeaders(_buffer, out consumed, out examined))
            {
                ErrorUtilities.ThrowInvalidRequestHeaders();
            }
        }

        private void ParseRequestLine()
        {
            _http1Connection.Reset();

            if (!_http1Connection.TakeStartLine(_buffer, out var consumed, out var examined))
            {
                ErrorUtilities.ThrowInvalidRequestLine();
            }
        }

        private void ParseRequestHeaders()
        {
            _http1Connection.Reset();

            if (!_http1Connection.TakeMessageHeaders(_buffer, out var consumed, out var examined))
            {
                ErrorUtilities.ThrowInvalidRequestHeaders();
            }
        }
    }
}
