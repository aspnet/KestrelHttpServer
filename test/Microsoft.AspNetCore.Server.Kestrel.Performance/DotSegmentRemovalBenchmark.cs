// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class DotSegmentRemovalBenchmark
    {
        private const int InnerLoopCount = 512;

        private const string _noSegments = "/request/target";
        private const string _segments = "/request/./target/../other";

        private readonly byte[] _noSegmentsBytes = Encoding.ASCII.GetBytes(_noSegments);
        private readonly byte[] _segmentsBytes = Encoding.ASCII.GetBytes(_segments);

        [Benchmark(Baseline = true, OperationsPerInvoke = InnerLoopCount)]
        public void StringNoSegments()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                var result = PathNormalizer.RemoveDotSegments(_noSegments);
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void StringSegments()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                var result = PathNormalizer.RemoveDotSegments(_segments);
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void SpanNoSegments()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                var result = PathNormalizer.RemoveDotSegments(new Span<byte>(_noSegmentsBytes, _noSegmentsBytes.Length));
            }
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void SpanSegments()
        {
            for (var i = 0; i < InnerLoopCount; i++)
            {
                var result = PathNormalizer.RemoveDotSegments(new Span<byte>(_segmentsBytes, _segmentsBytes.Length));
            }
        }
    }
}
