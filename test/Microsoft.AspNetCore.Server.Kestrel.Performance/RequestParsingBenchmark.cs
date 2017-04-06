// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class RequestParsingBenchmark
    {
        public IPipe Pipe { get; set; }

        public Frame<object> Frame { get; set; }

        public PipeFactory PipelineFactory { get; set; }

        [Setup]
        public void Setup()
        {
            var serviceContext = new ServiceContext
            {
                HttpParserFactory = f => new KestrelHttpParser(f.ServiceContext.Log),
                ServerOptions = new KestrelServerOptions()
            };
            var frameContext = new FrameContext
            {
                ServiceContext = serviceContext,
                ConnectionInformation = new MockConnectionInformation()
            };

            Frame = new Frame<object>(application: null, frameContext: frameContext);
            PipelineFactory = new PipeFactory();
            Pipe = PipelineFactory.Create();
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
        public void PlaintextAbsoluteUri()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextAbsoluteUriRequest);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void PipelinedPlaintextTechEmpower()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerPipelinedRequests);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void PipelinedPlaintextTechEmpowerTryRead()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerPipelinedRequests);
                ParseDataTryRead();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void PipelinedPlaintextTechEmpowerDrainBuffer()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerPipelinedRequests);
                ParseDataDrainBuffer();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void PipelinedPlaintextTechEmpowerDrainBufferAsync()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerPipelinedRequests);
                ParseDataDrainBufferAsync().GetAwaiter().GetResult();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void PipelinedPlaintextTechEmpowerAsync()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerPipelinedRequests);
                ParseDataAsync().GetAwaiter().GetResult();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void PipelinedPlaintextTechEmpowerAsyncHybridDrain()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.PlaintextTechEmpowerPipelinedRequests);
                ParseDataAsyncHybridDrain().GetAwaiter().GetResult();
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

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void PipelinedLiveAspNet()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.LiveaspnetPipelinedRequests);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount)]
        public void Unicode()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.UnicodeRequest);
                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.InnerLoopCount * RequestParsingData.Pipelining)]
        public void UnicodePipelined()
        {
            for (var i = 0; i < RequestParsingData.InnerLoopCount; i++)
            {
                InsertData(RequestParsingData.UnicodePipelinedRequests);
                ParseData();
            }
        }

        private void InsertData(byte[] bytes)
        {
            var buffer = Pipe.Writer.Alloc(2048);
            buffer.WriteFast(bytes);
            // There should not be any backpressure and task completes immediately
            buffer.FlushAsync().GetAwaiter().GetResult();
        }

        private void ParseDataDrainBuffer()
        {
            if (!Pipe.Reader.TryRead(out var result))
            {
                // No data?
                return;
            }

            var buffer = result.Buffer;
            var examined = buffer.End;
            var consumed = buffer.End;

            do
            {
                Frame.Reset();

                ParseRequest(ref buffer, out consumed, out examined);
            }
            while (buffer.Length > 0);

            Pipe.Reader.Advance(consumed, examined);
        }

        private async Task ParseDataDrainBufferAsync()
        {
            var result = await Pipe.Reader.ReadAsync();
            var buffer = result.Buffer;
            var examined = buffer.End;
            var consumed = buffer.End;

            do
            {
                Frame.Reset();

                ParseRequest(ref buffer, out consumed, out examined);
            }
            while (buffer.Length > 0);

            Pipe.Reader.Advance(consumed, examined);
        }

        private void ParseDataTryRead()
        {
            do
            {
                Frame.Reset();

                if (!Pipe.Reader.TryRead(out var result))
                {
                    // No more data
                    return;
                }

                var buffer = result.Buffer;
                var examined = buffer.End;
                var consumed = buffer.End;

                try
                {
                    ParseRequest(ref buffer, out consumed, out examined);
                }
                finally
                {
                    Pipe.Reader.Advance(consumed, examined);
                }
            }
            while (true);
        }

        private async Task ParseDataAsync()
        {
            do
            {
                Frame.Reset();

                var result = await Pipe.Reader.ReadAsync();
                var buffer = result.Buffer;
                var examined = buffer.End;
                var consumed = buffer.End;

                try
                {
                    ParseRequest(ref buffer, out consumed, out examined);
                }
                finally
                {
                    Pipe.Reader.Advance(consumed, examined);
                }
            }
            while (true);
        }

        private async Task ParseDataAsyncHybridDrain()
        {
            var buffer = default(ReadableBuffer);
            var examined = default(ReadCursor);
            var consumed = default(ReadCursor);
            var needBuffer = true;

            do
            {
                Frame.Reset();

                if (needBuffer)
                {
                    var result = await Pipe.Reader.ReadAsync();
                    buffer = result.Buffer;
                    needBuffer = false;
                }
                
                try
                {
                    ParseRequest(ref buffer, out consumed, out examined);
                }
                finally
                {
                    if (buffer.Length == 0)
                    {
                        Pipe.Reader.Advance(consumed, examined);

                        needBuffer = true;
                    }
                }
            }
            while (true);
        }

        private void ParseData()
        {
            do
            {
                Frame.Reset();

                var awaitable = Pipe.Reader.ReadAsync().GetAwaiter();
                if (!awaitable.IsCompleted)
                {
                    // No more data
                    return;
                }

                var result = awaitable.GetResult();
                var buffer = result.Buffer;
                var examined = buffer.End;
                var consumed = buffer.End;

                try
                {
                    ParseRequest(ref buffer, out consumed, out examined);
                }
                finally
                {
                    Pipe.Reader.Advance(consumed, examined);
                }
            }
            while (true);
        }

        public void ParseRequest(ref ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined)
        {
            if (!Frame.TakeStartLine(buffer, out consumed, out examined))
            {
                ErrorUtilities.ThrowInvalidRequestLine();
            }

            buffer = buffer.Slice(consumed);

            Frame.InitializeHeaders();

            if (!Frame.TakeMessageHeaders(buffer, out consumed, out examined))
            {
                ErrorUtilities.ThrowInvalidRequestHeaders();
            }

            buffer = buffer.Slice(consumed);
        }
    }
}
