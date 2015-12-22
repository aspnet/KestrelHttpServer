﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    public static class MemoryPoolIterator2Extensions
    {
        private static Encoding _utf8 = Encoding.UTF8;

        public const string HttpConnectMethod = "CONNECT";
        public const string HttpDeleteMethod = "DELETE";
        public const string HttpGetMethod = "GET";
        public const string HttpHeadMethod = "HEAD";
        public const string HttpPatchMethod = "PATCH";
        public const string HttpPostMethod = "POST";
        public const string HttpPutMethod = "PUT";
        public const string HttpOptionsMethod = "OPTIONS";
        public const string HttpTraceMethod = "TRACE";

        public const string Http10Version = "HTTP/1.0";
        public const string Http11Version = "HTTP/1.1";

        // readonly primitive statics can be Jit'd to consts https://github.com/dotnet/coreclr/issues/1079
        private readonly static long _httpConnectMethodLong = GetAsciiStringAsLong("CONNECT\0");
        private readonly static long _httpDeleteMethodLong = GetAsciiStringAsLong("DELETE\0\0");
        private readonly static long _httpGetMethodLong = GetAsciiStringAsLong("GET\0\0\0\0\0");
        private readonly static long _httpHeadMethodLong = GetAsciiStringAsLong("HEAD\0\0\0\0");
        private readonly static long _httpPatchMethodLong = GetAsciiStringAsLong("PATCH\0\0\0");
        private readonly static long _httpPostMethodLong = GetAsciiStringAsLong("POST\0\0\0\0");
        private readonly static long _httpPutMethodLong = GetAsciiStringAsLong("PUT\0\0\0\0\0");
        private readonly static long _httpOptionsMethodLong = GetAsciiStringAsLong("OPTIONS\0");
        private readonly static long _httpTraceMethodLong = GetAsciiStringAsLong("TRACE\0\0\0");

        private readonly static long _http10VersionLong = GetAsciiStringAsLong("HTTP/1.0");
        private readonly static long _http11VersionLong = GetAsciiStringAsLong("HTTP/1.1");

        private const int PerfectHashDivisor = 37;
        private static Tuple<long, string>[] _knownStrings = new Tuple<long, string>[PerfectHashDivisor];

        static MemoryPoolIterator2Extensions()
        {
            _knownStrings[_httpConnectMethodLong % PerfectHashDivisor] = Tuple.Create(_httpConnectMethodLong, HttpConnectMethod);
            _knownStrings[_httpDeleteMethodLong % PerfectHashDivisor] = Tuple.Create(_httpDeleteMethodLong, HttpDeleteMethod);
            _knownStrings[_httpGetMethodLong % PerfectHashDivisor] = Tuple.Create(_httpGetMethodLong, HttpGetMethod);
            _knownStrings[_httpHeadMethodLong % PerfectHashDivisor] = Tuple.Create(_httpHeadMethodLong, HttpHeadMethod);
            _knownStrings[_httpPatchMethodLong % PerfectHashDivisor] = Tuple.Create(_httpPatchMethodLong, HttpPatchMethod);
            _knownStrings[_httpPostMethodLong % PerfectHashDivisor] = Tuple.Create(_httpPostMethodLong, HttpPostMethod);
            _knownStrings[_httpPutMethodLong % PerfectHashDivisor] = Tuple.Create(_httpPutMethodLong, HttpPutMethod);
            _knownStrings[_httpOptionsMethodLong % PerfectHashDivisor] = Tuple.Create(_httpOptionsMethodLong, HttpOptionsMethod);
            _knownStrings[_httpTraceMethodLong % PerfectHashDivisor] = Tuple.Create(_httpTraceMethodLong, HttpTraceMethod);
            _knownStrings[_http10VersionLong % PerfectHashDivisor] = Tuple.Create(_http10VersionLong, Http10Version);
            _knownStrings[_http11VersionLong % PerfectHashDivisor] = Tuple.Create(_http11VersionLong, Http11Version);
        }

        private unsafe static long GetAsciiStringAsLong(string str)
        {
            Debug.Assert(str.Length == 8, "String must be exactly 8 (ASCII) characters long.");

            var bytes = Encoding.ASCII.GetBytes(str);

            fixed (byte* ptr = bytes)
            {
                return *(long*)ptr;
            }
        }
        
        public unsafe static string GetAsciiString(this MemoryPoolIterator2 start, MemoryPoolIterator2 end)
        {
            if (start.IsDefault || end.IsDefault)
            {
                return null;
            }

            var length = start.GetLength(end);

            if (length == 0)
            {
                return null;
            }

            // Bytes out of the range of ascii are treated as "opaque data" 
            // and kept in string as a char value that casts to same input byte value
            // https://tools.ietf.org/html/rfc7230#section-3.2.4

            var inputOffset = start.Index;
            var block = start.Block;

            var asciiString = new string('\0', length);

            fixed (char* outputStart = asciiString)
            {
                var output = outputStart;
                var remaining = length;

                var endBlock = end.Block;
                var endIndex = end.Index;

                while (true)
                {
                    int following = (block != endBlock ? block.End : endIndex) - inputOffset;

                    if (following > 0)
                    {
                        var input = block.Pointer + inputOffset;
                        var i = 0;
                        while (i + 11 < following)
                        {
                            i += 12;
                            *(output) = (char)*(input);
                            *(output + 1) = (char)*(input + 1);
                            *(output + 2) = (char)*(input + 2);
                            *(output + 3) = (char)*(input + 3);
                            *(output + 4) = (char)*(input + 4);
                            *(output + 5) = (char)*(input + 5);
                            *(output + 6) = (char)*(input + 6);
                            *(output + 7) = (char)*(input + 7);
                            *(output + 8) = (char)*(input + 8);
                            *(output + 9) = (char)*(input + 9);
                            *(output + 10) = (char)*(input + 10);
                            *(output + 11) = (char)*(input + 11);
                            output += 12;
                            input += 12;
                        }
                        if (i + 6 < following)
                        {
                            i += 6;
                            *(output) = (char)*(input);
                            *(output + 1) = (char)*(input + 1);
                            *(output + 2) = (char)*(input + 2);
                            *(output + 3) = (char)*(input + 3);
                            *(output + 4) = (char)*(input + 4);
                            *(output + 5) = (char)*(input + 5);
                            output += 6;
                            input += 6;
                        }
                        if (i + 3 < following)
                        {
                            i += 4;
                            *(output) = (char)*(input);
                            *(output + 1) = (char)*(input + 1);
                            *(output + 2) = (char)*(input + 2);
                            *(output + 3) = (char)*(input + 3);
                            output += 4;
                            input += 4;
                        }
                        while (i < following)
                        {
                            i++;
                            *(output++) = (char)*(input++);
                        }
                        
                        remaining -= following;
                    }

                    if (remaining == 0)
                    {
                        break;
                    }

                    block = block.Next;
                    inputOffset = block.Start;
                }
            }

            return asciiString;
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

        public static ArraySegment<byte> GetArraySegment(this MemoryPoolIterator2 start, MemoryPoolIterator2 end, out byte[] rentedBuffer)
        {
            rentedBuffer = null;
            if (start.IsDefault || end.IsDefault)
            {
                return default(ArraySegment<byte>);
            }
            if (end.Block == start.Block)
            {
                return new ArraySegment<byte>(start.Block.Array, start.Index, end.Index - start.Index);
            }

            var length = start.GetLength(end);
            rentedBuffer = ArrayPool<byte>.Shared.Rent(length);
            start.CopyTo(rentedBuffer, 0, length, out length);
            return new ArraySegment<byte>(rentedBuffer, 0, length);
        }

        /// <summary>
        /// Checks that up to 8 bytes between <paramref name="begin"/> and <paramref name="end"/> correspond to a known HTTP string.
        /// </summary>
        /// <remarks>
        /// A "known HTTP string" can be an HTTP method name defined in the HTTP/1.1 RFC or an HTTP version (HTTP/1.0 or HTTP/1.1).
        /// Since all of those fit in at most 8 bytes, they can be optimally looked up by reading those bytes as a long. Once
        /// in that format, uninteresting bits are cleared and the remaining long modulo 37 is looked up in a table.
        /// The number 37 was chosen because that number allows for a perfect hash of the set of
        /// "known strings" (CONNECT, DELETE, GET, HEAD, PATCH, POST, PUT, OPTIONS, TRACE, HTTP/1.0 and HTTP/1.1, where strings
        /// with less than 8 characters have 0s appended to their ends to fill for the missing bytes).
        /// </remarks>
        /// <param name="begin">The iterator from which to start the known string lookup.</param>
        /// <param name="end">The iterator pointing to the end of the input string.</param>
        /// <param name="knownString">A reference to a pre-allocated known string, if the input matches any.</param>
        /// <returns><c>true</c> if the input matches a known string, <c>false</c> otherwise.</returns>
        public static bool GetKnownString(this MemoryPoolIterator2 begin, MemoryPoolIterator2 end, out string knownString)
        {
            knownString = null;

            // This optimization only works on little endian environments (for now).
            if (!BitConverter.IsLittleEndian)
            {
                return false;
            }

            var inputLength = begin.GetLength(end);

            if (inputLength > sizeof(long))
            {
                return false;
            }

            var inputLong = begin.PeekLong();

            if (inputLong == -1)
            {
                return false;
            }

            inputLong &= (long)(unchecked((ulong)~0) >> ((sizeof(long) - inputLength) * 8));

            var value = _knownStrings[inputLong % PerfectHashDivisor];
            if (value != null && value.Item1 == inputLong)
            {
                knownString = value.Item2;
            }

            return knownString != null;
        }
    }
}
