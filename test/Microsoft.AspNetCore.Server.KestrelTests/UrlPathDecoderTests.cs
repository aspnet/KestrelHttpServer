// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class UrlPathDecoderTests
    {
        [Fact]
        public void Empty()
        {
            using (var pool = new MemoryPool())
            {
                var mem = pool.Lease();

                PositiveAssert(mem, string.Empty, string.Empty);

                pool.Return(mem);
            }
        }

        [Fact]
        public void WhiteSpace()
        {
            using (var pool = new MemoryPool())
            {
                var mem = pool.Lease();

                PositiveAssert(mem, "    ", "    ");

                pool.Return(mem);
            }
        }

        [Theory]
        [InlineData("/foo/bar", "/foo/bar")]
        [InlineData("/foo/BAR", "/foo/BAR")]
        [InlineData("/foo/", "/foo/")]
        [InlineData("/", "/")]
        public void NormalCases(string raw, string expect)
        {
            using (var pool = new MemoryPool())
            {
                var mem = pool.Lease();

                PositiveAssert(mem, raw, expect);

                pool.Return(mem);
            }
        }

        [Theory]
        [InlineData("%2F", "%2F")]
        [InlineData("/foo%2Fbar", "/foo%2Fbar")]
        [InlineData("/foo%2F%20bar", "/foo%2F bar")]
        public void SkipForwardSlash(string raw, string expect)
        {
            using (var pool = new MemoryPool())
            {
                var mem = pool.Lease();

                PositiveAssert(mem, raw, expect);

                pool.Return(mem);
            }
        }

        [Theory]
        [InlineData("%D0%A4", "Ф")]
        [InlineData("%d0%a4", "Ф")]
        [InlineData("%E0%A4%AD", "भ")]
        [InlineData("%e0%A4%Ad", "भ")]
        [InlineData("%F0%A4%AD%A2", "𤭢")]
        [InlineData("%F0%a4%Ad%a2", "𤭢")]
        [InlineData("%48%65%6C%6C%6F%20%57%6F%72%6C%64", "Hello World")]
        [InlineData("%48%65%6C%6C%6F%2D%C2%B5%40%C3%9F%C3%B6%C3%A4%C3%BC%C3%A0%C3%A1", "Hello-µ@ßöäüàá")]
        // Test the borderline cases of overlong UTF8.
        [InlineData("%C2%80", "\u0080")]
        [InlineData("%E0%A0%80", "\u0800")]
        [InlineData("%F0%90%80%80", "\U00010000")]
        [InlineData("%63", "c")]
        [InlineData("%32", "2")]
        [InlineData("%20", " ")]
        public void ValidUTF8(string raw, string expect)
        {
            using (var pool = new MemoryPool())
            {
                var mem = pool.Lease();

                PositiveAssert(mem, raw, expect);

                pool.Return(mem);
            }
        }

        [Theory]
        [InlineData("%C3%84ra%20Benetton", "Ära Benetton")]
        [InlineData("%E6%88%91%E8%87%AA%E6%A8%AA%E5%88%80%E5%90%91%E5%A4%A9%E7%AC%91%E5%8E%BB%E7%95%99%E8%82%9D%E8%83%86%E4%B8%A4%E6%98%86%E4%BB%91", "我自横刀向天笑去留肝胆两昆仑")]
        public void Internationalized(string raw, string expect)
        {
            using (var pool = new MemoryPool())
            {
                var mem = pool.Lease();

                PositiveAssert(mem, raw, expect);

                pool.Return(mem);
            }
        }

        [Theory]
        // Overlong ASCII
        [InlineData("%C0%A4", "%C0%A4")]
        [InlineData("%C1%BF", "%C1%BF")]
        [InlineData("%E0%80%AF", "%E0%80%AF")]
        [InlineData("%E0%9F%BF", "%E0%9F%BF")]
        [InlineData("%F0%80%80%AF", "%F0%80%80%AF")]
        [InlineData("%F0%8F%8F%BF", "%F0%8F%8F%BF")]
        // Incomplete
        [InlineData("%", "%")]
        [InlineData("%%", "%%")]
        [InlineData("%A", "%A")]
        [InlineData("%Y", "%Y")]
        // Mixed
        [InlineData("%%32", "%2")]
        [InlineData("%%20", "% ")]
        [InlineData("%C0%A4%32", "%C0%A42")]
        [InlineData("%32%C0%A4%32", "2%C0%A42")]
        [InlineData("%C0%32%A4", "%C02%A4")]
        public void InvalidUTF8(string raw, string expect)
        {
            using (var pool = new MemoryPool())
            {
                var mem = pool.Lease();

                PositiveAssert(mem, raw, expect);

                pool.Return(mem);
            }
        }

        [Theory]
        [InlineData("/foo%2Fbar", 10, "/foo%2Fbar", 10)]
        [InlineData("/foo%2Fbar", 9, "/foo%2Fba", 9)]
        [InlineData("/foo%2Fbar", 8, "/foo%2Fb", 8)]
        [InlineData("%D0%A4", 6, "Ф", 1)]
        [InlineData("%D0%A4", 5, "%D0%A", 5)]
        [InlineData("%D0%A4", 4, "%D0%", 4)]
        [InlineData("%D0%A4", 3, "%D0", 3)]
        [InlineData("%D0%A4", 2, "%D", 2)]
        [InlineData("%D0%A4", 1, "%", 1)]
        [InlineData("%D0%A4", 0, "", 0)]
        [InlineData("%C2%B5%40%C3%9F%C3%B6%C3%A4%C3%BC%C3%A0%C3%A1", 45, "µ@ßöäüàá", 8)]
        [InlineData("%C2%B5%40%C3%9F%C3%B6%C3%A4%C3%BC%C3%A0%C3%A1", 44, "µ@ßöäüà%C3%A", 12)]
        public void DecodeWithBoundary(string raw, int rawLength, string expect, int expectLength)
        {
            using (var pool = new MemoryPool())
            {
                var mem = pool.Lease();

                var begin = BuildSample(mem, raw);
                var end = GetIterator(begin, rawLength);

                MemoryPoolIterator end2;
                Assert.True(UrlPathDecoder.TryUnescape(begin, end, out end2));
                var result = begin.GetUtf8String(ref end2);

                Assert.Equal(expectLength, result.Length);
                Assert.Equal(expect, result);

                pool.Return(mem);
            }
        }

        [Theory]
        [InlineData("%00")]
        [InlineData("a%00")]
        [InlineData("%00b")]
        [InlineData("a%00b")]
        public void TryUnescapeReturnsFalseOnNullCharacter(string raw)
        {
            using (var pool = new MemoryPool())
            {
                var mem = pool.Lease();

                var begin = BuildSample(mem, raw);
                var end = GetIterator(begin, raw.Length);

                MemoryPoolIterator end2;
                Assert.False(UrlPathDecoder.TryUnescape(begin, end, out end2));

                pool.Return(mem);
            }
        }

        private MemoryPoolIterator BuildSample(MemoryPoolBlock mem, string data)
        {
            var store = data.Select(c => (byte)c).ToArray();
            mem.GetIterator().CopyFrom(new ArraySegment<byte>(store));

            return mem.GetIterator();
        }

        private MemoryPoolIterator GetIterator(MemoryPoolIterator begin, int displacement)
        {
            var result = begin;
            for (int i = 0; i < displacement; ++i)
            {
                result.Take();
            }

            return result;
        }

        private void PositiveAssert(MemoryPoolBlock mem, string raw, string expect)
        {
            var begin = BuildSample(mem, raw);
            var end = GetIterator(begin, raw.Length);

            MemoryPoolIterator result;
            Assert.True(UrlPathDecoder.TryUnescape(begin, end, out result));
            Assert.Equal(expect, begin.GetUtf8String(ref result));
        }

        private void PositiveAssert(MemoryPoolBlock mem, string raw)
        {
            var begin = BuildSample(mem, raw);
            var end = GetIterator(begin, raw.Length);


            MemoryPoolIterator result;
            Assert.True(UrlPathDecoder.TryUnescape(begin, end, out result));
            Assert.NotEqual(raw.Length, begin.GetUtf8String(ref result).Length);
        }

        private void NegativeAssert(MemoryPoolBlock mem, string raw)
        {
            var begin = BuildSample(mem, raw);
            var end = GetIterator(begin, raw.Length);

            MemoryPoolIterator resultEnd;
            Assert.True(UrlPathDecoder.TryUnescape(begin, end, out resultEnd));
            var result = begin.GetUtf8String(ref resultEnd);
            Assert.Equal(raw, result);
        }
    }
}
