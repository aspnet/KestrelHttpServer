// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public struct HttpRequestLineParseInfo
    {
        // Http10 = 0,
        // Http11 = 1,
        // HttpMethod: 2 - 32
        private const int PathEncoded = 64;
        private const int PathContainsDots = 128;

        private int _details;

        public HttpRequestLineParseInfo(HttpMethod method)
        {
            _details = ((int)method << 1);
        }

        public HttpVersion HttpVersion
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // Get version (Note does not work with Unknown version; which is -1)
                // The parser should throw for Unknown. Could throw in the set
                // but the parser's throw will contain more context.
                return (HttpVersion)(_details & 1);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                // Clear and set version
                _details = (_details & ~1) | (int)value;
            }
        }

        public HttpMethod HttpMethod
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // HttpMethod is shifted up one bit; shift down and mask
                return (HttpMethod)((_details >> 1) & 0xF);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                // HttpMethod is shifted up one bit; clear and shift up and set
                _details = (_details & ~0x1E) | (int)value << 1;
            }
        }
        public bool IsPathEncoded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // Check flag
                return (_details & PathEncoded) != 0;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (value)
                {
                    // Set flag
                    _details |= PathEncoded;
                }
                else
                {
                    // Clear flag
                    _details &= ~PathEncoded;
                }
            }
        }
        public bool DoesPathContainDots
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // Check flag
                return (_details & PathContainsDots) != 0;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (value)
                {
                    // Set flag
                    _details |= PathContainsDots;
                }
                else
                {
                    // Clear flag
                    _details &= ~PathContainsDots;
                }
            }
        }
    }
}
