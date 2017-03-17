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
        // Immutable
        private const string _noSegments = "/long/request/target/for/benchmarking/what/else/can/we/put/here";
        private const string _singleDotSegments = "/long/./request/./target/./for/./benchmarking/./what/./else/./can/./we/./put/./here";
        private const string _doubleDotSegments = "/long/../request/../target/../for/../benchmarking/../what/../else/../can/../we/../put/../here";

        private readonly char[] _noSegmentsChars = _noSegments.ToCharArray();
        private readonly char[] _singleDotSegmentsChars = _singleDotSegments.ToCharArray();
        private readonly char[] _doubleDotSegmentsChars = _doubleDotSegments.ToCharArray();

        private readonly byte[] _noSegmentsAscii = Encoding.ASCII.GetBytes(_noSegments);
        private readonly byte[] _singleDotSegmentsAscii = Encoding.ASCII.GetBytes(_singleDotSegments);
        private readonly byte[] _doubleDotSegmentsAscii = Encoding.ASCII.GetBytes(_doubleDotSegments);

        // Mutable
        private readonly char[] _noSegmentsString = new char[_noSegments.Length];
        private readonly char[] _singleDotSegmentsString = new char[_singleDotSegments.Length];
        private readonly char[] _doubleDotSegmentsString = new char[_doubleDotSegments.Length];

        private readonly byte[] _noSegmentsBytes = new byte[_noSegments.Length];
        private readonly byte[] _singleDotSegmentsBytes = new byte[_singleDotSegments.Length];
        private readonly byte[] _doubleDotSegmentsBytes = new byte[_doubleDotSegments.Length];

        [Benchmark(Baseline = true)]
        public unsafe int StringNoSegments()
        {
            _noSegmentsChars.CopyTo(_noSegmentsString);

            fixed (char* start = _noSegmentsString)
            {
                return PathNormalizer.RemoveDotSegments(start, start + _noSegments.Length);
            }
        }

        [Benchmark]
        public unsafe int StringSingleDotSegments()
        {
            _singleDotSegmentsChars.CopyTo(_singleDotSegmentsString);

            fixed (char* start = _singleDotSegmentsString)
            {
                return PathNormalizer.RemoveDotSegments(start, start + _noSegments.Length);
            }
        }

        [Benchmark]
        public unsafe int StringDoubleDotSegments()
        {
            _doubleDotSegmentsChars.CopyTo(_doubleDotSegmentsString);

            fixed (char* start = _doubleDotSegmentsString)
            {
                return PathNormalizer.RemoveDotSegments(start, start + _doubleDotSegments.Length);
            }
        }

        [Benchmark]
        public unsafe int SpanNoSegments()
        {
            _noSegmentsAscii.CopyTo(_noSegmentsBytes);

            fixed (byte* start = _noSegmentsBytes)
            {
                return PathNormalizer.RemoveDotSegments(start, start + _noSegments.Length);
            }
        }

        [Benchmark]
        public unsafe int SpanSingleDotSegments()
        {
            _singleDotSegmentsAscii.CopyTo(_singleDotSegmentsBytes);

            fixed (byte* start = _singleDotSegmentsBytes)
            {
                return PathNormalizer.RemoveDotSegments(start, start + _singleDotSegments.Length);
            }
        }

        [Benchmark]
        public unsafe int SpanDoubleDotSegments()
        {
            _doubleDotSegmentsAscii.CopyTo(_doubleDotSegmentsBytes);

            fixed (byte* start = _doubleDotSegmentsBytes)
            {
                return PathNormalizer.RemoveDotSegments(start, start + _doubleDotSegments.Length);
            }
        }
    }
}
