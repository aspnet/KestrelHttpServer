﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
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
        public void RemovesDotSegmentsSpan(string expected, string input)
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
