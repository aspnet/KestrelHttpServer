// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public struct HttpRequestLineParseInfo
    {
        // ** Bit usage
        // Http10 =                                              0b0000_0000_0000_0000
        // Http11 =                                              0b0000_0000_0000_0001
        private const uint HttpMethodGetMask =                                  0b1111; // 0 - 15
        private const uint HttpMethodSetMask =                   0b0000_0000_0001_1110; // >> 1 = 0 - 15
        private const uint PathEncodedFlag =                     0b0000_0000_0010_0000; // 32
        private const uint PathContainsDotsFlag =                0b0000_0000_0100_0000; // 64
        private const uint QueryLengthMask = 0b1111_1111_1111_1111_1111_1111_1000_0000; // Max size 33,554,431‬ bytes (32MB)

        private uint _details;

        public HttpRequestLineParseInfo(HttpMethod method)
        {
            _details = ((uint)method << 1);
        }

        public HttpVersion HttpVersion
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return (HttpVersion)(_details & 1u);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (((uint)value & 1u) != (uint)value)
                {
                    RejectRequestUnrecognizedHTTPVersion();
                }
                // Clear and set version
                _details = (_details & ~1u) | (uint)value;
            }
        }

        public HttpMethod HttpMethod
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // HttpMethod is shifted up one bit; shift down and mask
                return (HttpMethod)((_details >> 1) & HttpMethodGetMask);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                // HttpMethod is shifted up one bit; clear and shift up and set
                _details = (_details & ~HttpMethodSetMask) | (uint)value << 1;
            }
        }
        public bool IsPathEncoded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // Check flag
                return (_details & PathEncodedFlag) != 0;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (value)
                {
                    // Set flag
                    _details |= PathEncodedFlag;
                }
                else
                {
                    // Clear flag
                    _details &= ~PathEncodedFlag;
                }
            }
        }
        public bool DoesPathContainDots
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // Check flag
                return (_details & PathContainsDotsFlag) != 0;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (value)
                {
                    // Set flag
                    _details |= PathContainsDotsFlag;
                }
                else
                {
                    // Clear flag
                    _details &= ~PathContainsDotsFlag;
                }
            }
        }

        public int QueryLength
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return (int)((_details & QueryLengthMask) >> 7);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                var val = (uint)value;
                if (val > 0x1ffffff)
                {
                    // Definately too long
                    RejectRequestRequestLineTooLong();
                }
                _details = (_details & ~QueryLengthMask) | val << 7;
            }
        }

        private static void RejectRequestRequestLineTooLong()
        {
            throw RejectRequest(RequestRejectionReason.RequestLineTooLong);
        }

        private static void RejectRequestUnrecognizedHTTPVersion()
        {
            throw RejectRequest(RequestRejectionReason.UnrecognizedHTTPVersion);
        }

        private static BadHttpRequestException RejectRequest(RequestRejectionReason reason)
        {
            return BadHttpRequestException.GetException(reason);
        }
    }
}
