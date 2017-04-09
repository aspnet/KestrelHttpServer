// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Testing;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class FrameWritingBenchmark
    {
        // Standard completed task
        private static readonly Task CompletedStandardTask = Task.CompletedTask;
        private static readonly Func<object, Task> _syncTask = (obj) => CompletedStandardTask;
        // Non-standard completed task
        private static readonly Task CompletedDifferentTask = Task.FromResult(27);
        private static readonly Func<object, Task> _asyncTask = (obj) => CompletedDifferentTask;

        private readonly TestFrame<object> _frame;
        private readonly IPipe _outputPipe;

        private readonly byte[] _writeData;

        public FrameWritingBenchmark()
        {
            var factory = new PipeFactory();

            _frame = MakeFrame(factory);
            _outputPipe = factory.Create();
            _frame.Output = new OutputProducer(_outputPipe.Writer, null, null, null);

            _writeData = Encoding.ASCII.GetBytes("Hello, World!");
        }

        [Params(true, false)]
        public bool WithHeaders { get; set; }

        [Params(true, false)]
        public bool Chunked { get; set; }

        [Params(Startup.None, Startup.Sync, Startup.Async)]
        public Startup OnStarting { get; set; }

        [Setup]
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
                _frame.Flush();
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
                        _frame.OnStarting(_syncTask, null);
                        break;
                    case Startup.Async:
                        _frame.OnStarting(_asyncTask, null);
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

        private TestFrame<object> MakeFrame(PipeFactory factory)
        {
            var socketInput = factory.Create();

            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                Log = new MockTrace(),
                HttpParserFactory = f => new HttpParser(log: null)
            };
            var frameContext = new FrameContext
            {
                ServiceContext = serviceContext,
                ConnectionInformation = new MockConnectionInformation()
            };

            var frame = new TestFrame<object>(application: null, context: frameContext)
            {
                Input = socketInput.Reader
            };

            frame.Reset();
            frame.InitializeHeaders();
            frame.InitializeStreams(MessageBody.ZeroContentLengthKeepAlive);

            return frame;
        }

        [Cleanup]
        public void Cleanup()
        {
            EmptyPipe(_outputPipe.Reader);
        }

        private void EmptyPipe(IPipeReader reader)
        {
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
