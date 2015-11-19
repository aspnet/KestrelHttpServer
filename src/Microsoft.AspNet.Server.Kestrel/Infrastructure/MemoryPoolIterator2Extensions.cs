// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    public static class MemoryPoolIterator2Extensions
    {
        private const int _maxStackAllocBytes = 16384;

        private static Encoding _utf8 = Encoding.UTF8;
        private static ulong _startHash;

        // hash bits = random _startHash xor
        // 63 62 61 60 59 58 57 56 55 54 53 52 51 50 49 48 47 46 45 44 43 42 41 40 39 38 37 36 35 34 33 32 
        // | length & 0xff << 56 | |------   xor ((byte << (index & 0xf) << 2) & 0xffffff) << 32)  ------|
        // 31 30 29 28 27 26 25 24 23 22 21 20 19 18 17 16 15 14 13 12 11 10  9  8  7  6  5  4  3  2  1  0
        // |-----------------------      xor (byte << ((index << 3) & 0x1f))      -----------------------|

        static MemoryPoolIterator2Extensions()
        {
            using (var rnd = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                var randomBytes = new byte[8];
                rnd.GetBytes(randomBytes);
                _startHash =
                    ((ulong)randomBytes[0]) |
                    (((ulong)randomBytes[1]) << 8) |
                    (((ulong)randomBytes[2]) << 16) |
                    (((ulong)randomBytes[3]) << 24) |
                    (((ulong)randomBytes[4]) << 32) |
                    (((ulong)randomBytes[5]) << 40) |
                    (((ulong)randomBytes[6]) << 48) |
                    (((ulong)randomBytes[7]) << 56);
            }
        }

        private static unsafe string GetAsciiStringStack(byte[] input, int inputOffset, int length, StringPool stringPool)
        {
            // avoid declaring other local vars, or doing work with stackalloc
            // to prevent the .locals init cil flag , see: https://github.com/dotnet/coreclr/issues/1279
            char* output = stackalloc char[length];

            return GetAsciiStringImplementation(output, input, inputOffset, length, stringPool);
        }
        private static unsafe string GetAsciiStringImplementation(char* output, byte[] input, int inputOffset, int length, StringPool stringPool)
        {
            var hash = _startHash ^ (((ulong)length & 0xff) << 56);

            for (var i = 0; i < length; i++)
            {
                var b = input[inputOffset + i];
                output[i] = (char)b;

                hash ^= (((ulong)(b << ((i & 0xf) << 2)) & 0xffffff) << 32) | ((ulong)b << ((i << 3) & 0x1f));
            }

            if (stringPool != null)
            {
                return stringPool.GetString(hash, output, length);
            }

            return new string(output, 0, length);
        }

        private static unsafe string GetAsciiStringStack(MemoryPoolBlock2 start, MemoryPoolIterator2 end, int inputOffset, int length, StringPool stringPool)
        {
            // avoid declaring other local vars, or doing work with stackalloc
            // to prevent the .locals init cil flag , see: https://github.com/dotnet/coreclr/issues/1279
            char* output = stackalloc char[length];

            return GetAsciiStringImplementation(output, start, end, inputOffset, length, stringPool);
        }

        private unsafe static string GetAsciiStringHeap(MemoryPoolBlock2 start, MemoryPoolIterator2 end, int inputOffset, int length, StringPool stringPool)
        {
            var buffer = new char[length];

            fixed (char* output = buffer)
            {
                return GetAsciiStringImplementation(output, start, end, inputOffset, length, stringPool);
            }
        }

        private static unsafe string GetAsciiStringImplementation(char* output, MemoryPoolBlock2 start, MemoryPoolIterator2 end, int inputOffset, int length, StringPool stringPool)
        {
            var hash = _startHash ^ (((ulong)length & 0xff) << 56);

            var outputOffset = 0;
            var block = start;
            var remaining = length;

            var endBlock = end.Block;
            var endIndex = end.Index;

            while (true)
            {
                int following = (block != endBlock ? block.End : endIndex) - inputOffset;

                if (following > 0)
                {
                    var input = block.Array;
                    for (var i = 0; i < following; i++)
                    {
                        var b = input[inputOffset + i];

                        output[i + outputOffset] = (char)b;

                        hash ^=  (((ulong)(b << ((i & 0xf) << 2)) & 0xffffff) << 32) | ((ulong)b << ((i << 3) & 0x1f));
                    }

                    remaining -= following;
                    outputOffset += following;
                }

                if (remaining == 0)
                {
                    if (stringPool != null)
                    {
                        return stringPool.GetString(hash, output, length);
                    }

                    return new string(output, 0, length);
                }

                block = block.Next;
                inputOffset = block.Start;
            }
        }

        public static string GetAsciiString(this MemoryPoolIterator2 start, MemoryPoolIterator2 end, StringPool stringPool)
        {
            if (start.IsDefault || end.IsDefault)
            {
                return default(string);
            }

            var length = start.GetLength(end);

            // Bytes out of the range of ascii are treated as "opaque data" 
            // and kept in string as a char value that casts to same input byte value
            // https://tools.ietf.org/html/rfc7230#section-3.2.4
            if (end.Block == start.Block)
            {
                return GetAsciiStringStack(start.Block.Array, start.Index, length, stringPool);
            }

            if (length > _maxStackAllocBytes)
            {
                return GetAsciiStringHeap(start.Block, end, start.Index, length, stringPool);
            }

            return GetAsciiStringStack(start.Block, end, start.Index, length, stringPool);
        }

        public static string GetUtf8String(this MemoryPoolIterator2 start, MemoryPoolIterator2 end)
        {
            if (start.IsDefault || end.IsDefault)
            {
                return default(string);
            }
            if (end.Block == start.Block)
            {
                return _utf8.GetString(start.Block.Array, start.Index, end.Index - start.Index);
            }

            var decoder = _utf8.GetDecoder();

            var length = start.GetLength(end);
            var charLength = length * 2;
            var chars = new char[charLength];
            var charIndex = 0;

            var block = start.Block;
            var index = start.Index;
            var remaining = length;
            while (true)
            {
                int bytesUsed;
                int charsUsed;
                bool completed;
                var following = block.End - index;
                if (remaining <= following)
                {
                    decoder.Convert(
                        block.Array,
                        index,
                        remaining,
                        chars,
                        charIndex,
                        charLength - charIndex,
                        true,
                        out bytesUsed,
                        out charsUsed,
                        out completed);
                    return new string(chars, 0, charIndex + charsUsed);
                }
                else if (block.Next == null)
                {
                    decoder.Convert(
                        block.Array,
                        index,
                        following,
                        chars,
                        charIndex,
                        charLength - charIndex,
                        true,
                        out bytesUsed,
                        out charsUsed,
                        out completed);
                    return new string(chars, 0, charIndex + charsUsed);
                }
                else
                {
                    decoder.Convert(
                        block.Array,
                        index,
                        following,
                        chars,
                        charIndex,
                        charLength - charIndex,
                        false,
                        out bytesUsed,
                        out charsUsed,
                        out completed);
                    charIndex += charsUsed;
                    remaining -= following;
                    block = block.Next;
                    index = block.Start;
                }
            }
        }

        public static ArraySegment<byte> GetArraySegment(this MemoryPoolIterator2 start, MemoryPoolIterator2 end)
        {
            if (start.IsDefault || end.IsDefault)
            {
                return default(ArraySegment<byte>);
            }
            if (end.Block == start.Block)
            {
                return new ArraySegment<byte>(start.Block.Array, start.Index, end.Index - start.Index);
            }

            var length = start.GetLength(end);
            var array = new byte[length];
            start.CopyTo(array, 0, length, out length);
            return new ArraySegment<byte>(array, 0, length);
        }
    }
}
