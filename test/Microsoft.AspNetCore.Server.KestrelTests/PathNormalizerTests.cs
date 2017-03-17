// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class PathNormalizerTests
    {
        [Theory]
        [MemberData(nameof(DotSegmentData))]
        public void RemovesDotSegmentsSpan(string input, string expected)
        {
            var data = Encoding.ASCII.GetBytes(input);
            var length = PathNormalizer.RemoveDotSegments(new Span<byte>(data));
            Assert.True(length >= 1);
            Assert.Equal(expected, Encoding.ASCII.GetString(data, 0, length));
        }

        [Theory]
        [MemberData(nameof(DotSegmentData))]
        public void RemovesDotSegmentsString(string input, string expected)
        {
            var length = PathNormalizer.RemoveDotSegments(input);
            Assert.True(length >= 1);
            Assert.Equal(expected, input.Substring(0, length));
        }

        public static TheoryData<string, string> DotSegmentData = new TheoryData<string, string>
        {
            { "/a", "/a" },
            { "/a/", "/a/" },
            { "/a/b", "/a/b" },
            { "/a/b/", "/a/b/" },
            { "/./a", "/a" },
            { "/././a", "/a" },
            { "/../a", "/a" },
            { "/../../a", "/a" },
            { "/a/./b", "/a/b" },
            { "/a/../b", "/b" },
            { "/a/./", "/a/" },
            { "/a/.", "/a/" },
            { "/a/../", "/" },
            { "/a/..", "/" },
            { "/a/../b/../", "/" },
            { "/a/../b/..", "/" },
            { "/a/../../b", "/b" },
            { "/a/../../b/", "/b/" },
            { "/a/.././../b", "/b" },
            { "/a/.././../b/", "/b/" },
            { "/a/b/c/./../../d", "/a/d" },
            { "/./a/b/c/./../../d", "/a/d" },
            { "/../a/b/c/./../../d", "/a/d" },
            { "/./../a/b/c/./../../d", "/a/d" },
            { "/.././a/b/c/./../../d", "/a/d" },
            { "/.a", "/.a" },
            { "/..a", "/..a" },
            { "/...", "/..." },
            { "/a/.../b", "/a/.../b" },
            { "/a/../.../../b", "/b" },
            { "/a/.b", "/a/.b" },
            { "/a/..b", "/a/..b" },
            { "/a/b.", "/a/b." },
            { "/a/b..", "/a/b.." },
            { "/longlong/../short", "/short" },
            { "/short/../longlong", "/longlong" },
            { "/longlong/../short/..", "/" },
            { "/short/../longlong/..", "/" },
            { "/longlong/../short/../", "/" },
            { "/short/../longlong/../", "/" },
            { "/", "/" },
            { "/no/segments", "/no/segments" },
            { "/no/segments/", "/no/segments/" },
        };
    }
}
