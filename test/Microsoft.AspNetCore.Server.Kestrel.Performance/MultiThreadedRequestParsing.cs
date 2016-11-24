// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using RequestLineStatus = Microsoft.AspNetCore.Server.Kestrel.Internal.Http.Frame.RequestLineStatus;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class MultiThreadedRequestParsing
    {
        // Is divided by processor count so will be slightly off as the 
        // LCM of Intel's core counts (22, 20, 18, 16, 14, 12, 10, 8, 6, 4, 2) is 55440 
        // which would be far too many iterations
        private const int InnerLoopCount = 4096;

        private static readonly int ThreadCount = Environment.ProcessorCount;
        private static readonly int LoopCount = InnerLoopCount / ThreadCount;

        private static MemoryPool[] MemoryPool;
        private static SocketInput[] Input;
        private static Frame<object>[] Frame;

        [Benchmark(Baseline = true, OperationsPerInvoke = InnerLoopCount)]
        public void ParsePlaintext()
        {
            Parallel.For(0, ThreadCount, new ParallelOptions() {MaxDegreeOfParallelism = ThreadCount}, (index) =>
            {
                var socketInput = Input[index];
                var frame = Frame[index];

                for (var i = 0; i < LoopCount; i++)
                {
                    InsertData(socketInput, Requests.PlaintextRequest);

                    ParseData(socketInput, frame);
                }
            });
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount * Requests.Pipelining)]
        public void ParsePipelinedPlaintext()
        {
            Parallel.For(0, ThreadCount, new ParallelOptions() { MaxDegreeOfParallelism = ThreadCount }, (index) =>
            {
                var socketInput = Input[index];
                var frame = Frame[index];

                for (var i = 0; i < LoopCount; i++)
                {
                    InsertData(socketInput, Requests.PlaintextPipelinedRequests);

                    ParseData(socketInput, frame);
                }
            });
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void ParseLiveAspNet()
        {
            Parallel.For(0, ThreadCount, new ParallelOptions() { MaxDegreeOfParallelism = ThreadCount }, (index) =>
            {
                var socketInput = Input[index];
                var frame = Frame[index];

                for (var i = 0; i < LoopCount; i++)
                {
                    InsertData(socketInput, Requests.LiveaspnentRequest);

                    ParseData(socketInput, frame);
                }
            });
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount * Requests.Pipelining)]
        public void ParsePipelinedLiveAspNet()
        {
            Parallel.For(0, ThreadCount, new ParallelOptions() { MaxDegreeOfParallelism = ThreadCount }, (index) =>
            {
                var socketInput = Input[index];
                var frame = Frame[index];

                for (var i = 0; i < LoopCount; i++)
                {
                    InsertData(socketInput, Requests.LiveaspnentPipelinedRequests);

                    ParseData(socketInput, frame);
                }
            });
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void ParseUnicode()
        {
            Parallel.For(0, ThreadCount, new ParallelOptions() { MaxDegreeOfParallelism = ThreadCount }, (index) =>
            {
                var socketInput = Input[index];
                var frame = Frame[index];

                for (var i = 0; i < LoopCount; i++)
                {
                    InsertData(socketInput, Requests.UnicodeRequest);

                    ParseData(socketInput, frame);
                }
            });
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount * Requests.Pipelining)]
        public void ParseUnicodePipelined()
        {
            Parallel.For(0, ThreadCount, new ParallelOptions() { MaxDegreeOfParallelism = ThreadCount }, (index) =>
            {
                var socketInput = Input[index];
                var frame = Frame[index];

                for (var i = 0; i < LoopCount; i++)
                {
                    InsertData(socketInput, Requests.UnicodePipelinedRequests);

                    ParseData(socketInput, frame);
                }
            });
        }

        private static void InsertData(SocketInput socketInput, byte[] dataBytes)
        {
            socketInput.IncomingData(dataBytes, 0, dataBytes.Length);
        }

        private static void ParseData(SocketInput socketInput, Frame<object> frame)
        {
            while (socketInput.GetAwaiter().IsCompleted)
            {
                frame.Reset();

                if (frame.TakeStartLine(socketInput) != RequestLineStatus.Done)
                {
                    ThrowInvalidStartLine();
                }

                frame.InitializeHeaders();

                if (!frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders))
                {
                    ThrowInvalidMessageHeaders();
                }
            }
        }

        private static void ThrowInvalidStartLine()
        {
            throw new InvalidOperationException("Invalid StartLine");
        }

        private static void ThrowInvalidMessageHeaders()
        {
            throw new InvalidOperationException("Invalid MessageHeaders");
        }

        [Setup]
        public void Setup()
        {
            var threadCount = ThreadCount;

            MemoryPool = new MemoryPool[threadCount];
            Input = new SocketInput[threadCount];
            Frame = new Frame<object>[threadCount];

            for (var i = 0; i < threadCount; i++)
            {
                Requests.SetupFrameObjects(out MemoryPool[i], out Input[i], out Frame[i]);
            }

        }

        [Cleanup]
        public void Cleanup()
        {
            var threadCount = ThreadCount;
            for (var i = 0; i < threadCount; i++)
            {
                Requests.CleanUpFrameObjects(ref MemoryPool[i], ref Input[i], ref Frame[i]);
            }
        }
    }
}
