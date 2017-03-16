// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class DotSegmentRemovalBenchmark
    {
        private const string _noSegments = "/long/request/target/for/benchmarking/what/else/can/we/put/here";
        private const string _singleDotSegments = "/long/./request/./target/./for/./benchmarking/./what/./else/./can/./we/./put/./here";
        private const string _doubleDotSegments = "/long/../request/../target/../for/../benchmarking/../what/../else/../can/../we/../put/../here";

        private readonly byte[] _noSegmentsBytes = Encoding.ASCII.GetBytes(_noSegments);
        private readonly byte[] _singleDotSegmentsBytes = Encoding.ASCII.GetBytes(_singleDotSegments);
        private readonly byte[] _doubleDotSegmentsBytes = Encoding.ASCII.GetBytes(_doubleDotSegments);

        [Benchmark]
        public string StringNoSegments()
            => PathNormalizer.RemoveDotSegments(_noSegments);

        [Benchmark]
        public string StringSingleDotSegments()
            => PathNormalizer.RemoveDotSegments(_singleDotSegments);

        [Benchmark]
        public string StringDoubleDotSegments()
            => PathNormalizer.RemoveDotSegments(_doubleDotSegments);

        [Benchmark]
        public int SpanNoSegments()
            => PathNormalizer.RemoveDotSegments(_noSegmentsBytes);

        [Benchmark]
        public int SpanSingleDotSegments()
            => PathNormalizer.RemoveDotSegments(_singleDotSegmentsBytes);

        [Benchmark]
        public int SpanDoubleDotSegments()
            => PathNormalizer.RemoveDotSegments(_doubleDotSegmentsBytes);
    }
}
