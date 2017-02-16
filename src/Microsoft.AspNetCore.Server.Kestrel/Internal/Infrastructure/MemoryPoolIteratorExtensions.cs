// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure
{
    public static class MemoryPoolIteratorExtensions
    {
        public const string Http10Version = "HTTP/1.0";
        public const string Http11Version = "HTTP/1.1";

        public const string HttpScheme = "http://";
        public const string HttpsScheme = "https://";

        // readonly primitive statics can be Jit'd to consts https://github.com/dotnet/coreclr/issues/1079
        private readonly static ulong _httpConnectMethodLong = GetAsciiStringAsLong("CONNECT ");
        private readonly static ulong _httpDeleteMethodLong = GetAsciiStringAsLong("DELETE \0");
        private readonly static ulong _httpGetMethodLong = GetAsciiStringAsLong("GET \0\0\0\0");
        private readonly static ulong _httpHeadMethodLong = GetAsciiStringAsLong("HEAD \0\0\0");
        private readonly static ulong _httpPatchMethodLong = GetAsciiStringAsLong("PATCH \0\0");
        private readonly static ulong _httpPostMethodLong = GetAsciiStringAsLong("POST \0\0\0");
        private readonly static ulong _httpPutMethodLong = GetAsciiStringAsLong("PUT \0\0\0\0");
        private readonly static ulong _httpOptionsMethodLong = GetAsciiStringAsLong("OPTIONS ");
        private readonly static ulong _httpTraceMethodLong = GetAsciiStringAsLong("TRACE \0\0");

        private readonly static ulong _http10VersionLong = GetAsciiStringAsLong("HTTP/1.0");
        private readonly static ulong _http11VersionLong = GetAsciiStringAsLong("HTTP/1.1");

        private readonly static ulong _httpSchemeLong = GetAsciiStringAsLong("http://\0");
        private readonly static ulong _httpsSchemeLong = GetAsciiStringAsLong("https://");

        private readonly static ulong _mask8Chars = GetMaskAsLong(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff });
        private readonly static ulong _mask7Chars = GetMaskAsLong(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 });
        private readonly static ulong _mask6Chars = GetMaskAsLong(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00 });
        private readonly static ulong _mask5Chars = GetMaskAsLong(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00 });
        private readonly static ulong _mask4Chars = GetMaskAsLong(new byte[] { 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00 });

        private readonly static Tuple<ulong, ulong, string>[] _knownMethods = new Tuple<ulong, ulong, string>[8];

        static MemoryPoolIteratorExtensions()
        {
            _knownMethods[0] = Tuple.Create(_mask4Chars, _httpPutMethodLong, HttpMethods.Put);
            _knownMethods[1] = Tuple.Create(_mask5Chars, _httpPostMethodLong, HttpMethods.Post);
            _knownMethods[2] = Tuple.Create(_mask5Chars, _httpHeadMethodLong, HttpMethods.Head);
            _knownMethods[3] = Tuple.Create(_mask6Chars, _httpTraceMethodLong, HttpMethods.Trace);
            _knownMethods[4] = Tuple.Create(_mask6Chars, _httpPatchMethodLong, HttpMethods.Patch);
            _knownMethods[5] = Tuple.Create(_mask7Chars, _httpDeleteMethodLong, HttpMethods.Delete);
            _knownMethods[6] = Tuple.Create(_mask8Chars, _httpConnectMethodLong, HttpMethods.Connect);
            _knownMethods[7] = Tuple.Create(_mask8Chars, _httpOptionsMethodLong, HttpMethods.Options);
        }

        private unsafe static ulong GetAsciiStringAsLong(string str)
        {
            Debug.Assert(str.Length == 8, "String must be exactly 8 (ASCII) characters long.");

            var bytes = Encoding.ASCII.GetBytes(str);

            fixed (byte* ptr = &bytes[0])
            {
                return *(ulong*)ptr;
            }
        }
        private unsafe static ulong GetMaskAsLong(byte[] bytes)
        {
            Debug.Assert(bytes.Length == 8, "Mask must be exactly 8 bytes long.");

            fixed (byte* ptr = bytes)
            {
                return *(ulong*)ptr;
            }
        }

        public static string GetAsciiStringEscaped(this MemoryPoolIterator start, MemoryPoolIterator end, int maxChars)
        {
            var sb = new StringBuilder();
            var scan = start;

            while (maxChars > 0 && (scan.Block != end.Block || scan.Index != end.Index))
            {
                var ch = scan.Take();
                sb.Append(ch < 0x20 || ch >= 0x7F ? $"<0x{ch.ToString("X2")}>" : ((char)ch).ToString());
                maxChars--;
            }

            if (scan.Block != end.Block || scan.Index != end.Index)
            {
                sb.Append("...");
            }

            return sb.ToString();
        }

        public static ArraySegment<byte> PeekArraySegment(this MemoryPoolIterator iter)
        {
            if (iter.IsDefault || iter.IsEnd)
            {
                return default(ArraySegment<byte>);
            }

            if (iter.Index < iter.Block.End)
            {
                return new ArraySegment<byte>(iter.Block.Array, iter.Index, iter.Block.End - iter.Index);
            }

            var block = iter.Block.Next;
            while (block != null)
            {
                if (block.Start < block.End)
                {
                    return new ArraySegment<byte>(block.Array, block.Start, block.End - block.Start);
                }
                block = block.Next;
            }

            // The following should be unreachable due to the IsEnd check above.
            throw new InvalidOperationException("This should be unreachable!");
        }

        /// <summary>
        /// Checks that up to 8 bytes from <paramref name="begin"/> correspond to a known HTTP method.
        /// </summary>
        /// <remarks>
        /// A "known HTTP method" can be an HTTP method name defined in the HTTP/1.1 RFC.
        /// Since all of those fit in at most 8 bytes, they can be optimally looked up by reading those bytes as a long. Once
        /// in that format, it can be checked against the known method.
        /// The Known Methods (CONNECT, DELETE, GET, HEAD, PATCH, POST, PUT, OPTIONS, TRACE) are all less than 8 bytes
        /// and will be compared with the required space. A mask is used if the Known method is less than 8 bytes.
        /// To optimize performance the GET method will be checked first.
        /// </remarks>
        /// <param name="begin">The iterator from which to start the known string lookup.</param>
        /// <param name="knownMethod">A reference to a pre-allocated known string, if the input matches any.</param>
        /// <returns><c>true</c> if the input matches a known string, <c>false</c> otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetKnownMethod(this MemoryPoolIterator begin, out string knownMethod)
        {
            knownMethod = null;

            ulong value;
            if (!begin.TryPeekLong(out value))
            {
                return false;
            }

            if ((value & _mask4Chars) == _httpGetMethodLong)
            {
                knownMethod = HttpMethods.Get;
                return true;
            }
            foreach (var x in _knownMethods)
            {
                if ((value & x.Item1) == x.Item2)
                {
                    knownMethod = x.Item3;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks 8 bytes from <paramref name="begin"/> that correspond to 'http://' or 'https://'
        /// </summary>
        /// <param name="begin">The iterator</param>
        /// <param name="knownScheme">A reference to the known scheme, if the input matches any</param>
        /// <returns>True when memory starts with known http or https schema</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetKnownHttpSchema(this MemoryPoolIterator begin, out string knownScheme)
        {
            knownScheme = null;
            ulong value;
            if (!begin.TryPeekLong(out value))
            {
                return false;
            }

            if ((value & _mask7Chars) == _httpSchemeLong)
            {
                knownScheme = HttpScheme;
                return true;
            }

            if (value == _httpsSchemeLong)
            {
                knownScheme = HttpsScheme;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks 9 bytes from <paramref name="begin"/>  correspond to a known HTTP version.
        /// </summary>
        /// <remarks>
        /// A "known HTTP version" Is is either HTTP/1.0 or HTTP/1.1.
        /// Since those fit in 8 bytes, they can be optimally looked up by reading those bytes as a long. Once
        /// in that format, it can be checked against the known versions.
        /// The Known versions will be checked with the required '\r'.
        /// To optimize performance the HTTP/1.1 will be checked first.
        /// </remarks>
        /// <param name="begin">The iterator from which to start the known string lookup.</param>
        /// <param name="knownVersion">A reference to a pre-allocated known string, if the input matches any.</param>
        /// <returns><c>true</c> if the input matches a known string, <c>false</c> otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetKnownVersion(this MemoryPoolIterator begin, out string knownVersion)
        {
            knownVersion = null;

            ulong value;
            if (!begin.TryPeekLong(out value))
            {
                return false;
            }

            if (value == _http11VersionLong)
            {
                knownVersion = Http11Version;
            }
            else if (value == _http10VersionLong)
            {
                knownVersion = Http10Version;
            }

            if (knownVersion != null)
            {
                begin.Skip(knownVersion.Length);

                if (begin.Peek() != '\r')
                {
                    knownVersion = null;
                }
            }

            return knownVersion != null;
        }
    }
}
