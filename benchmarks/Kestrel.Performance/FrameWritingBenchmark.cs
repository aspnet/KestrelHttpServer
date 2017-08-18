﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Testing;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class FrameWritingBenchmark
    {
        // Standard completed task
        private static readonly Func<object, Task> _syncTaskFunc = (obj) => Task.CompletedTask;
        // Non-standard completed task
        private static readonly Task _psuedoAsyncTask = Task.FromResult(27);
        private static readonly Func<object, Task> _psuedoAsyncTaskFunc = (obj) => _psuedoAsyncTask;

        private readonly TestFrame<object> _frame;
        private readonly IPipe _outputPipe;

        private readonly byte[] _writeData;

        public FrameWritingBenchmark()
        {
            var pipeFactory = new PipeFactory();

            _outputPipe = pipeFactory.Create();
            _frame = MakeFrame(pipeFactory);
            _writeData = Encoding.ASCII.GetBytes("Hello, World!");
        }

        [Params(true, false)]
        public bool WithHeaders { get; set; }

        [Params(true, false)]
        public bool Chunked { get; set; }

        [Params(Startup.None, Startup.Sync, Startup.Async)]
        public Startup OnStarting { get; set; }

        [IterationSetup]
        public void Setup()
        {
            _frame.Reset();
            if (Chunked)
            {
                _frame.RequestHeaders.Add("Transfer-Encoding", "chunked");
            }
            else
            {
                _frame.RequestHeaders.ContentLength = _writeData.Length;
            }

            if (!WithHeaders)
            {
                _frame.FlushAsync().GetAwaiter().GetResult();
            }

            ResetState();
        }

        private void ResetState()
        {
            if (WithHeaders)
            {
                _frame.ResetState();

                switch (OnStarting)
                {
                    case Startup.Sync:
                        _frame.OnStarting(_syncTaskFunc, null);
                        break;
                    case Startup.Async:
                        _frame.OnStarting(_psuedoAsyncTaskFunc, null);
                        break;
                }
            }
        }

        [Benchmark]
        public Task WriteAsync()
        {
            ResetState();

            return _frame.ResponseBody.WriteAsync(_writeData, 0, _writeData.Length, default(CancellationToken));
        }

        private TestFrame<object> MakeFrame(PipeFactory pipeFactory)
        {
            var input = pipeFactory.Create();

            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                Log = new MockTrace(),
                HttpParserFactory = f => new HttpParser<FrameAdapter>()
            };

            var frame = new TestFrame<object>(application: null, context: new FrameContext
            {
                ServiceContext = serviceContext,
                PipeFactory = pipeFactory,
                Input = input.Reader,
                Output = _outputPipe 
            });

            frame.Reset();
            frame.InitializeStreams(MessageBody.ZeroContentLengthKeepAlive);

            return frame;
        }

        [IterationCleanup]
        public void Cleanup()
        {
            var reader = _outputPipe.Reader;
            if (reader.TryRead(out var readResult))
            {
                reader.Advance(readResult.Buffer.End);
            }
        }

        public enum Startup
        {
            None,
            Sync,
            Async
        }
    }
}
