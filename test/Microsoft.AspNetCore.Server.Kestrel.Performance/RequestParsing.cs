// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using RequestLineStatus = Microsoft.AspNetCore.Server.Kestrel.Internal.Http.Frame.RequestLineStatus;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class RequestParsing
    {
        private const int InnerLoopCount = 512;

        private MemoryPool MemoryPool;
        private SocketInput Input;
        private Frame<object> Frame;

        [Benchmark(Baseline = true, OperationsPerInvoke = InnerLoopCount)]
        public void ParsePlaintext()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                InsertData(Requests.PlaintextRequest);

                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount * Requests.Pipelining)]
        public void ParsePipelinedPlaintext()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                InsertData(Requests.PlaintextPipelinedRequests);

                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void ParseLiveAspNet()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                InsertData(Requests.LiveaspnentRequest);

                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount * Requests.Pipelining)]
        public void ParsePipelinedLiveAspNet()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                InsertData(Requests.LiveaspnentPipelinedRequests);

                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void ParseUnicode()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                InsertData(Requests.UnicodeRequest);

                ParseData();
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount * Requests.Pipelining)]
        public void ParseUnicodePipelined()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                InsertData(Requests.UnicodePipelinedRequests);

                ParseData();
            }
        }

        private void InsertData(byte[] dataBytes)
        {
            Input.IncomingData(dataBytes, 0, dataBytes.Length);
        }

        private void ParseData()
        {
            while (Input.GetAwaiter().IsCompleted)
            {
                Frame.Reset();

                if (Frame.TakeStartLine(Input) != RequestLineStatus.Done)
                {
                    ThrowInvalidStartLine();
                }

                Frame.InitializeHeaders();

                if (!Frame.TakeMessageHeaders(Input, (FrameRequestHeaders) Frame.RequestHeaders))
                {
                    ThrowInvalidMessageHeaders();
                }
            }
        }

        private void ThrowInvalidStartLine()
        {
            throw new InvalidOperationException("Invalid StartLine");
        }

        private void ThrowInvalidMessageHeaders()
        {
            throw new InvalidOperationException("Invalid MessageHeaders");
        }

        [Setup]
        public void Setup()
        {
            Requests.SetupFrameObjects(out MemoryPool, out Input, out Frame);
        }

        [Cleanup]
        public void Cleanup()
        {
            Requests.CleanUpFrameObjects(ref MemoryPool, ref Input, ref Frame);
        }
    }
}
