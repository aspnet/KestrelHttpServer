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
        [InlineData("/a", "/a")]
        [InlineData("/a/", "/a/")]
        [InlineData("/a/b", "/a/b")]
        [InlineData("/a/b/", "/a/b/")]
        [InlineData("/./a", "/a")]
        [InlineData("/././a", "/a")]
        [InlineData("/../a", "/a")]
        [InlineData("/../../a", "/a")]
        [InlineData("/a/./b", "/a/b")]
        [InlineData("/a/../b", "/b")]
        [InlineData("/a/./", "/a/")]
        [InlineData("/a/.", "/a/")]
        [InlineData("/a/../", "/")]
        [InlineData("/a/..", "/")]
        [InlineData("/a/../b/../", "/")]
        [InlineData("/a/../b/..", "/")]
        [InlineData("/a/../../b", "/b")]
        [InlineData("/a/../../b/", "/b/")]
        [InlineData("/a/.././../b", "/b")]
        [InlineData("/a/.././../b/", "/b/")]
        [InlineData("/a/b/c/./../../d", "/a/d")]
        [InlineData("/./a/b/c/./../../d", "/a/d")]
        [InlineData("/../a/b/c/./../../d", "/a/d")]
        [InlineData("/./../a/b/c/./../../d", "/a/d")]
        [InlineData("/.././a/b/c/./../../d", "/a/d")]
        [InlineData("/.a", "/.a")]
        [InlineData("/..a", "/..a")]
        [InlineData("/...", "/...")]
        [InlineData("/a/.../b", "/a/.../b")]
        [InlineData("/a/../.../../b", "/b")]
        [InlineData("/a/.b", "/a/.b")]
        [InlineData("/a/..b", "/a/..b")]
        [InlineData("/a/b.", "/a/b.")]
        [InlineData("/a/b..", "/a/b..")]
        [InlineData("/longlong/../short", "/short")]
        [InlineData("/short/../longlong", "/longlong")]
        [InlineData("/longlong/../short/..", "/")]
        [InlineData("/short/../longlong/..", "/")]
        [InlineData("/longlong/../short/../", "/")]
        [InlineData("/short/../longlong/../", "/")]
        [InlineData("/", "/")]
        [InlineData("/no/segments", "/no/segments")]
        [InlineData("/no/segments/", "/no/segments/")]
        public void RemovesDotSegmentsSpan(string input, string expected)
        {
            var data = Encoding.ASCII.GetBytes(input);
            var finalLength = PathNormalizer.RemoveDotSegments(new Span<byte>(data));
            Assert.True(finalLength >= 1);
            Assert.Equal(expected, Encoding.ASCII.GetString(data, 0, finalLength));
        }

        [Theory]
        [InlineData("/a", "/a")]
        [InlineData("/a/", "/a/")]
        [InlineData("/a/b", "/a/b")]
        [InlineData("/a/b/", "/a/b/")]
        [InlineData("/./a", "/a")]
        [InlineData("/././a", "/a")]
        [InlineData("/../a", "/a")]
        [InlineData("/../../a", "/a")]
        [InlineData("/a/./b", "/a/b")]
        [InlineData("/a/../b", "/b")]
        [InlineData("/a/./", "/a/")]
        [InlineData("/a/.", "/a")]
        [InlineData("/a/../b/../", "/")]
        [InlineData("/a/../b/..", "/")]
        [InlineData("/a/../../b", "/b")]
        [InlineData("/a/../../b/", "/b/")]
        [InlineData("/a/.././../b", "/b")]
        [InlineData("/a/.././../b/", "/b/")]
        [InlineData("/a/b/c/./../../d", "/a/d")]
        [InlineData("/./a/b/c/./../../d", "/a/d")]
        [InlineData("/../a/b/c/./../../d", "/a/d")]
        [InlineData("/./../a/b/c/./../../d", "/a/d")]
        [InlineData("/.././a/b/c/./../../d", "/a/d")]
        [InlineData("/.a", "/.a")]
        [InlineData("/..a", "/..a")]
        [InlineData("/...", "/...")]
        [InlineData("/a/.../b", "/a/.../b")]
        [InlineData("/a/../.../../b", "/b")]
        [InlineData("/a/.b", "/a/.b")]
        [InlineData("/a/..b", "/a/..b")]
        [InlineData("/a/b.", "/a/b.")]
        [InlineData("/a/b..", "/a/b..")]
        [InlineData("a/b", "a/b")]
        [InlineData("a/b/../c", "a/c")]
        [InlineData("*", "*")]
        [InlineData("/longlong/../short", "/short")]
        [InlineData("/short/../longlong", "/longlong")]
        [InlineData("/longlong/../short/..", "/")]
        [InlineData("/short/../longlong/..", "/")]
        [InlineData("/longlong/../short/../", "/")]
        [InlineData("/short/../longlong/../", "/")]
        [InlineData("/", "/")]
        [InlineData("/no/segments", "/no/segments")]
        [InlineData("/no/segments/", "/no/segments/")]
        // Not handled by span version
        [InlineData("./a", "a")]
        [InlineData("../a", "a")]
        [InlineData("./a/b", "a/b")]
        [InlineData("../a/../b", "/b")]
        [InlineData(".", "/")]
        [InlineData("..", "/")]
        [InlineData("*", "*")]
        public void RemovesDotSegmentsString(string input, string expected)
        {
            var result = PathNormalizer.RemoveDotSegments(input);
            Assert.Equal(expected, result);
        }
    }
}
