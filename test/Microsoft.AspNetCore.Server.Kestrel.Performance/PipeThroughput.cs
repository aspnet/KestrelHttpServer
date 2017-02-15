// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Testing;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class PipeThroughput
    {
        private IPipe Pipe;
        public PipeFactory PipelineFactory;

        private static readonly byte[] _plaintextRequest = Encoding.ASCII.GetBytes(plaintextRequest);
        private const string plaintextRequest = "GET /plaintext HTTP/1.1\r\nHost: www.example.com\r\n\r\n";

        private const int InnerLoopCount = 512;

        [Setup]
        public void Setup()
        {
            PipelineFactory = new PipeFactory();
            Pipe = PipelineFactory.Create();
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void ParseLiveAspNetTwoTasks()
        {
            var writing = Task.Run(async () =>
            {
                for (int i = 0; i < InnerLoopCount; i++)
                {
                    var writableBuffer = Pipe.Writer.Alloc(_plaintextRequest.Length);
                    writableBuffer.Advance(_plaintextRequest.Length);
                    await writableBuffer.FlushAsync();
                }
            });

            var reading = Task.Run(async () =>
            {
                int remaining = InnerLoopCount * _plaintextRequest.Length;
                while (remaining != 0)
                {
                    var result = await Pipe.Reader.ReadAsync();
                    remaining -= result.Buffer.Length;
                    Pipe.Reader.Advance(result.Buffer.End, result.Buffer.End);
                }
            });

            Task.WaitAll(writing, reading);
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void ParseLiveAspNetInline()
        {
            for (int i = 0; i < InnerLoopCount; i++)
            {
                var writableBuffer = Pipe.Writer.Alloc(_plaintextRequest.Length);
                _plaintextRequest.CopyTo(writableBuffer.Memory.Span);
                writableBuffer.Advance(_plaintextRequest.Length);
                writableBuffer.FlushAsync().GetAwaiter().GetResult();
                var result = Pipe.Reader.ReadAsync().GetAwaiter().GetResult();
                Pipe.Reader.Advance(result.Buffer.End, result.Buffer.End);
            }
        }
    }
}
