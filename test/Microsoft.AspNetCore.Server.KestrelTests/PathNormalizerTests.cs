﻿// Copyright (c) .NET Foundation. All rights reserved.
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
        [InlineData("a/b", "a/b")]
        [InlineData("a/b/../c", "a/c")]
        [InlineData("./a", "a")]
        [InlineData("../a", "a")]
        [InlineData("./a/b", "a/b")]
        [InlineData("../a/../b", "/b")]
        [InlineData(".", "/")]
        [InlineData("..", "/")]
        [InlineData("*", "*")]
        [InlineData("/longlong/../short", "/short")]
        [InlineData("/short/../longlong", "/longlong")]
        [InlineData("/longlong/../short/..", "/")]
        [InlineData("/short/../longlong/..", "/")]
        [InlineData("/longlong/../short/../", "/")]
        [InlineData("/short/../longlong/../", "/")]
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
        [InlineData("/a", "/./a")]
        [InlineData("/a", "/././a")]
        [InlineData("/a", "/../a")]
        [InlineData("/a", "/../../a")]
        [InlineData("/a/b", "/a/./b")]
        [InlineData("/b", "/a/../b")]
        [InlineData("/a/", "/a/./")]
        [InlineData("/a", "/a/.")]
        [InlineData("/", "/a/../b/../")]
        [InlineData("/", "/a/../b/..")]
        [InlineData("/b", "/a/../../b")]
        [InlineData("/b/", "/a/../../b/")]
        [InlineData("/b", "/a/.././../b")]
        [InlineData("/b/", "/a/.././../b/")]
        [InlineData("/a/d", "/a/b/c/./../../d")]
        [InlineData("/a/d", "/./a/b/c/./../../d")]
        [InlineData("/a/d", "/../a/b/c/./../../d")]
        [InlineData("/a/d", "/./../a/b/c/./../../d")]
        [InlineData("/a/d", "/.././a/b/c/./../../d")]
        [InlineData("/.a", "/.a")]
        [InlineData("/..a", "/..a")]
        [InlineData("/...", "/...")]
        [InlineData("/a/.../b", "/a/.../b")]
        [InlineData("/b", "/a/../.../../b")]
        [InlineData("/a/.b", "/a/.b")]
        [InlineData("/a/..b", "/a/..b")]
        [InlineData("/a/b.", "/a/b.")]
        [InlineData("/a/b..", "/a/b..")]
        [InlineData("a/b", "a/b")]
        [InlineData("a/c", "a/b/../c")]
        [InlineData("*", "*")]
        public void RemovesDotSegments(string expected, string input)
        {
            var result = PathNormalizer.RemoveDotSegments(input);
            Assert.Equal(expected, result);
        }
    }
}
