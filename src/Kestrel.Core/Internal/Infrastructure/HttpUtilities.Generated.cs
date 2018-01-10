// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public static partial class HttpUtilities
    {
        // readonly primitive statics can be Jit'd to consts https://github.com/dotnet/coreclr/issues/1079
        private static readonly ulong _httpConnectMethodLong = GetAsciiStringAsLong("CONNECT ");
        private static readonly ulong _httpDeleteMethodLong = GetAsciiStringAsLong("DELETE \0");
        private static readonly ulong _httpHeadMethodLong = GetAsciiStringAsLong("HEAD \0\0\0");
        private static readonly ulong _httpPatchMethodLong = GetAsciiStringAsLong("PATCH \0\0");
        private static readonly ulong _httpPostMethodLong = GetAsciiStringAsLong("POST \0\0\0");
        private static readonly ulong _httpPutMethodLong = GetAsciiStringAsLong("PUT \0\0\0\0");
        private static readonly ulong _httpOptionsMethodLong = GetAsciiStringAsLong("OPTIONS ");
        private static readonly ulong _httpTraceMethodLong = GetAsciiStringAsLong("TRACE \0\0");

        private static readonly ulong _mask8Chars = GetMaskAsLong(new byte[]
            {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff});

        private static readonly ulong _mask7Chars = GetMaskAsLong(new byte[]
            {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00});

        private static readonly ulong _mask6Chars = GetMaskAsLong(new byte[]
            {0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00});

        private static readonly ulong _mask5Chars = GetMaskAsLong(new byte[]
            {0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00});

        private static readonly ulong _mask4Chars = GetMaskAsLong(new byte[]
            {0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00});

        private static readonly Tuple<ulong, ulong, HttpMethod, int>[] _knownMethods =
            new Tuple<ulong, ulong, HttpMethod, int>[17];

        private static readonly string[] _methodNames = new string[9];

        static HttpUtilities()
        {
            SetKnownMethod(_mask4Chars, _httpPutMethodLong, String.Put, 3);
            SetKnownMethod(_mask5Chars, _httpHeadMethodLong, String.Head, 4);
            SetKnownMethod(_mask5Chars, _httpPostMethodLong, String.Post, 4);
            SetKnownMethod(_mask6Chars, _httpPatchMethodLong, String.Patch, 5);
            SetKnownMethod(_mask6Chars, _httpTraceMethodLong, String.Trace, 5);
            SetKnownMethod(_mask7Chars, _httpDeleteMethodLong, String.Delete, 6);
            SetKnownMethod(_mask8Chars, _httpConnectMethodLong, String.Connect, 7);
            SetKnownMethod(_mask8Chars, _httpOptionsMethodLong, String.Options, 7);
            FillKnownMethodsGaps();
            _methodNames[(byte)String.Connect] = HttpMethods.Connect;
            _methodNames[(byte)String.Delete] = HttpMethods.Delete;
            _methodNames[(byte)String.Get] = HttpMethods.Get;
            _methodNames[(byte)String.Head] = HttpMethods.Head;
            _methodNames[(byte)String.Options] = HttpMethods.Options;
            _methodNames[(byte)String.Patch] = HttpMethods.Patch;
            _methodNames[(byte)String.Post] = HttpMethods.Post;
            _methodNames[(byte)String.Put] = HttpMethods.Put;
            _methodNames[(byte)String.Trace] = HttpMethods.Trace;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetKnownMethodIndex(ulong value)
        {
            const int magicNumer = 0x600000C;
            var tmp = (int)value & magicNumer;
            return ((tmp >> 2) | (tmp >> 23)) & 0xF;
        }
    }
}