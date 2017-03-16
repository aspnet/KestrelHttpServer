// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public static class PathNormalizer
    {
        private const byte ByteSlash = (byte)'/';
        private const byte ByteDot = (byte)'.';

        private const char CharSlash = '/';
        private const char CharDot = '.';

        // In-place implementation of the algorithm from https://tools.ietf.org/html/rfc3986#section-5.2.4
        // The Span-based version is only used on origin-form targets, so it can assume the path
        // always start with a '/'.
        public static unsafe int RemoveDotSegments(Span<byte> input)
        {
            fixed (byte* start = &input.DangerousGetPinnableReference())
            {
                var end = start + input.Length;

                if (!ContainsDotSegments(start, end))
                {
                    return input.Length;
                }

                var src = start;
                var dst = start;

                while (src < end)
                {
                    var ch1 = *src;
                    Debug.Assert(ch1 == '/', "Path segment must always start with a '/'");

                    byte ch2, ch3, ch4;

                    switch (end - src)
                    {
                        case 1:
                            break;
                        case 2:
                            ch2 = *(src + 1);

                            if (ch2 == ByteDot)
                            {
                                // B.  if the input buffer begins with a prefix of "/./" or "/.",
                                //     where "." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer; otherwise,
                                src += 1;
                                *src = ByteSlash;
                                continue;
                            }

                            break;
                        case 3:
                            ch2 = *(src + 1);
                            ch3 = *(src + 2);

                            if (ch2 == ByteDot && ch3 == ByteDot)
                            {
                                // C.  if the input buffer begins with a prefix of "/../" or "/..",
                                //     where ".." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer and remove the last
                                //     segment and its preceding "/" (if any) from the output
                                //     buffer; otherwise,
                                src += 2;
                                *src = ByteSlash;

                                if (dst > start)
                                {
                                    do
                                    {
                                        dst--;
                                    } while (dst > start && *dst != ByteSlash);
                                }

                                continue;
                            }
                            else if (ch2 == ByteDot && ch3 == ByteSlash)
                            {
                                // B.  if the input buffer begins with a prefix of "/./" or "/.",
                                //     where "." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer; otherwise,
                                src += 2;
                                continue;
                            }

                            break;
                        default:
                            ch2 = *(src + 1);
                            ch3 = *(src + 2);
                            ch4 = *(src + 3);

                            if (ch2 == ByteDot && ch3 == ByteDot && ch4 == ByteSlash)
                            {
                                // C.  if the input buffer begins with a prefix of "/../" or "/..",
                                //     where ".." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer and remove the last
                                //     segment and its preceding "/" (if any) from the output
                                //     buffer; otherwise,
                                src += 3;

                                if (dst > start)
                                {
                                    do
                                    {
                                        dst--;
                                    } while (dst > start && *dst != ByteSlash);
                                }

                                continue;
                            }
                            else if (ch2 == ByteDot && ch3 == ByteSlash)
                            {
                                // B.  if the input buffer begins with a prefix of "/./" or "/.",
                                //     where "." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer; otherwise,
                                src += 2;
                                continue;
                            }

                            break;
                    }

                    // E.  move the first path segment in the input buffer to the end of
                    //     the output buffer, including the initial "/" character (if
                    //     any) and any subsequent characters up to, but not including,
                    //     the next "/" character or the end of the input buffer.
                    do
                    {
                        *dst++ = ch1;
                        ch1 = *++src;
                    } while (src < end && ch1 != ByteSlash);
                }

                if (dst == start)
                {
                    *dst++ = ByteSlash;
                }

                return (int)(dst - start);
            }
        }

        // In-place implementation of the algorithm from https://tools.ietf.org/html/rfc3986#section-5.2.4
        public static unsafe string RemoveDotSegments(string input)
        {
            if (!ContainsDotSegments(input))
            {
                return input;
            }

            var buffer = input.ToCharArray();

            fixed (char* start = buffer)
            {
                var end = start + input.Length;
                var src = start;
                var dst = start;

                while (src < end)
                {
                    var ch1 = *src;
                    char ch2, ch3, ch4;

                    switch (end - src)
                    {
                        case 1:
                            if (ch1 == CharDot)
                            {
                                // D.  if the input buffer consists only of "." or "..", then remove
                                //     that from the input buffer; otherwise,
                                src += 1;
                                continue;
                            }

                            break;
                        case 2:
                            ch2 = *(src + 1);

                            if (ch1 == CharDot && ch2 == CharSlash)
                            {
                                // A.  If the input buffer begins with a prefix of "../" or "./",
                                //     then remove that prefix from the input buffer; otherwise,
                                src += 2;
                                continue;
                            }
                            else if (ch1 == CharSlash && ch2 == CharDot)
                            {
                                // B.  if the input buffer begins with a prefix of "/./" or "/.",
                                //     where "." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer; otherwise,
                                src += 1;
                                *src = CharSlash;
                                continue;
                            }
                            else if (ch1 == CharDot && ch2 == CharDot)
                            {
                                // D.  if the input buffer consists only of "." or "..", then remove
                                //     that from the input buffer; otherwise,
                                src += 2;
                                continue;
                            }

                            break;
                        case 3:
                            ch2 = *(src + 1);
                            ch3 = *(src + 2);

                            if (ch1 == CharDot && ch2 == CharSlash)
                            {
                                // A.  If the input buffer begins with a prefix of "../" or "./",
                                //     then remove that prefix from the input buffer; otherwise,
                                src += 2;
                                continue;
                            }
                            else if (ch1 == CharDot && ch2 == CharDot && ch3 == CharSlash)
                            {
                                // A.  If the input buffer begins with a prefix of "../" or "./",
                                //     then remove that prefix from the input buffer; otherwise,
                                src += 3;
                                continue;
                            }
                            else if (ch1 == CharSlash && ch2 == CharDot && ch3 == CharSlash)
                            {
                                // B.  if the input buffer begins with a prefix of "/./" or "/.",
                                //     where "." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer; otherwise,
                                src += 2;
                                continue;
                            }
                            else if (ch1 == CharSlash && ch2 == CharDot && ch3 == CharDot)
                            {
                                // C.  if the input buffer begins with a prefix of "/../" or "/..",
                                //     where ".." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer and remove the last
                                //     segment and its preceding "/" (if any) from the output
                                //     buffer; otherwise,
                                src += 2;
                                *src = CharSlash;

                                if (dst > start)
                                {
                                    do
                                    {
                                        dst--;
                                    } while (dst > start && *dst != CharSlash);
                                }

                                continue;
                            }

                            break;
                        default:
                            ch2 = *(src + 1);
                            ch3 = *(src + 2);
                            ch4 = *(src + 3);

                            if (ch1 == CharDot && ch2 == CharSlash)
                            {
                                // A.  If the input buffer begins with a prefix of "../" or "./",
                                //     then remove that prefix from the input buffer; otherwise,
                                src += 2;
                                continue;
                            }
                            else if (ch1 == CharDot && ch2 == CharDot && ch3 == CharSlash)
                            {
                                // A.  If the input buffer begins with a prefix of "../" or "./",
                                //     then remove that prefix from the input buffer; otherwise,
                                src += 3;
                                continue;
                            }
                            else if (ch1 == CharSlash && ch2 == CharDot && ch3 == CharSlash)
                            {
                                // B.  if the input buffer begins with a prefix of "/./" or "/.",
                                //     where "." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer; otherwise,
                                src += 2;
                                continue;
                            }
                            else if (ch1 == CharSlash && ch2 == CharDot && ch3 == CharDot && ch4 == CharSlash)
                            {
                                // C.  if the input buffer begins with a prefix of "/../" or "/..",
                                //     where ".." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer and remove the last
                                //     segment and its preceding "/" (if any) from the output
                                //     buffer; otherwise,
                                src += 3;

                                if (dst > start)
                                {
                                    do
                                    {
                                        dst--;
                                    } while (dst > start && *dst != CharSlash);
                                }

                                continue;
                            }

                            break;
                    }

                    // E.  move the first path segment in the input buffer to the end of
                    //     the output buffer, including the initial "/" character (if
                    //     any) and any subsequent characters up to, but not including,
                    //     the next "/" character or the end of the input buffer.
                    do
                    {
                        *dst++ = *src++;
                    } while (src < end && *src != CharSlash);
                }

                if (dst == start)
                {
                    *dst++ = CharSlash;
                }

                return new string(buffer, 0, (int)(dst - start));
            }
        }

        public static unsafe bool ContainsDotSegments(byte* start, byte* end)
        {
            var src = start;
            var dst = start;

            while (src < end)
            {
                var ch1 = *src;
                Debug.Assert(ch1 == '/', "Path segment must always start with a '/'");

                byte ch2, ch3, ch4;

                switch (end - src)
                {
                    case 1:
                        break;
                    case 2:
                        ch2 = *(src + 1);

                        if (ch2 == ByteDot)
                        {
                            return true;
                        }

                        break;
                    case 3:
                        ch2 = *(src + 1);
                        ch3 = *(src + 2);

                        if ((ch2 == ByteDot && ch3 == ByteDot) ||
                            (ch2 == ByteDot && ch3 == ByteSlash))
                        {
                            return true;
                        }

                        break;
                    default:
                        ch2 = *(src + 1);
                        ch3 = *(src + 2);
                        ch4 = *(src + 3);

                        if ((ch2 == ByteDot && ch3 == ByteDot && ch4 == ByteSlash) ||
                            (ch2 == ByteDot && ch3 == ByteSlash))
                        {
                            return true;
                        }

                        break;
                }

                do
                {
                    ch1 = *++src;
                } while (src < end && ch1 != ByteSlash);
            }

            return false;
        }

        private unsafe static bool ContainsDotSegments(string input)
        {
            fixed (char* start = input)
            {
                var end = start + input.Length;
                var src = start;
                var dst = start;

                while (src < end)
                {
                    var ch1 = *src;
                    char ch2, ch3, ch4;

                    switch (end - src)
                    {
                        case 1:
                            if (ch1 == CharDot)
                            {
                                return true;
                            }

                            break;
                        case 2:
                            ch2 = *(src + 1);

                            if ((ch1 == CharDot && ch2 == CharSlash) ||
                                (ch1 == CharSlash && ch2 == CharDot) ||
                                (ch1 == CharDot && ch2 == CharDot))
                            {
                                return true;
                            }

                            break;
                        case 3:
                            ch2 = *(src + 1);
                            ch3 = *(src + 2);

                            if ((ch1 == CharDot && ch2 == CharSlash) ||
                                (ch1 == CharDot && ch2 == CharDot && ch3 == CharSlash) ||
                                (ch1 == CharSlash && ch2 == CharDot && ch3 == CharSlash) ||
                                (ch1 == CharSlash && ch2 == CharDot && ch3 == CharDot))
                            {
                                return true;
                            }

                            break;
                        default:
                            ch2 = *(src + 1);
                            ch3 = *(src + 2);
                            ch4 = *(src + 3);

                            if ((ch1 == CharDot && ch2 == CharSlash) ||
                                (ch1 == CharDot && ch2 == CharDot && ch3 == CharSlash) ||
                                (ch1 == CharSlash && ch2 == CharDot && ch3 == CharSlash) ||
                                (ch1 == CharSlash && ch2 == CharDot && ch3 == CharDot && ch4 == CharSlash))
                            {
                                return true;
                            }

                            break;
                    }

                    do
                    {
                        src++;
                    } while (src < end && *src != CharSlash);
                }

                return false;
            }
        }
    }
}
