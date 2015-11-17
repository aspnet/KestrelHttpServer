﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    public static class MemoryPoolIterator2Extenstions
    {
        private const int _maxStackAllocBytes = 16384;

        private static Encoding _utf8 = Encoding.UTF8;

        private static unsafe string GetAsciiStringStack(byte* input, int length)
        {
            // avoid declaring other local vars, or doing work with stackalloc
            // to prevent the .locals init cil flag , see: https://github.com/dotnet/coreclr/issues/1279
            char* output = stackalloc char[length];

            return GetAsciiStringImplementation(output, input, length);
        }
        private static unsafe string GetAsciiStringImplementation(char* output, byte* input, int length)
        {
            var outputStart = output;
            var i = 0;
            var lengthMinusSpan = length - 3;
            for (; i < lengthMinusSpan; i += 4)
            {
                *(output) = (char)*(input);
                *(output + 1) = (char)*(input + 1);
                *(output + 2) = (char)*(input + 2);
                *(output + 3) = (char)*(input + 3);
                output += 4;
                input += 4;
            }
            for (; i < length; i++)
            {
                *(output++) = (char)*(input++);
            }
            return new string(outputStart, 0, length);
        }

        private static unsafe string GetAsciiStringStack(MemoryPoolBlock2 start, MemoryPoolIterator2 end, int inputOffset, int length)
        {
            // avoid declaring other local vars, or doing work with stackalloc
            // to prevent the .locals init cil flag , see: https://github.com/dotnet/coreclr/issues/1279
            char* output = stackalloc char[length];

            return GetAsciiStringImplementation(output, start, end, inputOffset, length);
        }

        private unsafe static string GetAsciiStringHeap(MemoryPoolBlock2 start, MemoryPoolIterator2 end, int inputOffset, int length)
        {
            var buffer = new char[length];

            fixed (char* output = buffer)
            {
                return GetAsciiStringImplementation(output, start, end, inputOffset, length);
            }
        }

        private static unsafe string GetAsciiStringImplementation(char* output, MemoryPoolBlock2 start, MemoryPoolIterator2 end, int inputOffset, int length)
        {
            var outputStart = output;
            var block = start;
            var remaining = length;

            var endBlock = end.Block;
            var endIndex = end.Index;

            while (true)
            {
                int following = (block != endBlock ? block.End : endIndex) - inputOffset;

                if (following > 0)
                {
                    fixed (byte* blockStart = block.Array)
                    {
                        var input = blockStart + inputOffset;
                        var i = 0;
                        var followingMinusSpan = following - 3;
                        for (; i < followingMinusSpan; i += 4)
                        {
                            *(output) = (char)*(input);
                            *(output + 1) = (char)*(input + 1);
                            *(output + 2) = (char)*(input + 2);
                            *(output + 3) = (char)*(input + 3);
                            output += 4;
                            input += 4;
                        }
                        for (; i < following; i++)
                        {
                            *(output++) = (char)*(input++);
                        }
                    }
                    remaining -= following;
                }

                if (remaining == 0)
                {
                    return new string(outputStart, 0, length);
                }

                block = block.Next;
                inputOffset = block.Start;
            }
        }

        public unsafe static string GetAsciiString(this MemoryPoolIterator2 start, MemoryPoolIterator2 end)
        {
            if (start.IsDefault || end.IsDefault)
            {
                return default(string);
            }

            var length = start.GetLength(end);

            if (length <= 0)
            {
                return string.Empty;
            }

            // Bytes out of the range of ascii are treated as "opaque data" 
            // and kept in string as a char value that casts to same input byte value
            // https://tools.ietf.org/html/rfc7230#section-3.2.4
            if (end.Block == start.Block)
            {
                fixed (byte* input = start.Block.Array)
                {
                    return GetAsciiStringStack(input + start.Index, length);
                }
            }

            if (length > _maxStackAllocBytes)
            {
                return GetAsciiStringHeap(start.Block, end, start.Index, length);
            }

            return GetAsciiStringStack(start.Block, end, start.Index, length);
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

            var length = start.GetLength(end);

            if (length <= 0)
            {
                return string.Empty;
            }

            var decoder = _utf8.GetDecoder();
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
