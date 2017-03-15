// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public static class PathNormalizer
    {
        const byte Slash = (byte)'/';
        const byte Dot = (byte)'.';

        // In-place implementation of the algorithm from https://tools.ietf.org/html/rfc3986#section-5.2.4
        public static unsafe int RemoveDotSegments(Span<byte> span)
        {
            fixed (byte* start = &span.DangerousGetPinnableReference())
            {
                var end = start + span.Length;
                var src = start;
                var dst = start;

                while (src < end)
                {
                    var ch1 = *src; // Always '/'
                    byte ch2, ch3, ch4;

                    switch (end - src)
                    {
                        case 1:
                            //if (ch1 == Dot)
                            //{
                            //    // D.  if the input buffer consists only of "." or "..", then remove
                            //    //     that from the input buffer; otherwise,
                            //    src += 1;
                            //    continue;
                            //}

                            break;
                        case 2:
                            ch2 = *(src + 1);

                            if (/*ch1 == Slash &&*/ ch2 == Dot)
                            {
                                // B.  if the input buffer begins with a prefix of "/./" or "/.",
                                //     where "." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer; otherwise,
                                src += 1;
                                *src = Slash;
                                continue;
                            }
                            //else if (ch1 == Dot && ch2 == Slash)
                            //{
                            //    // A.  If the input buffer begins with a prefix of "../" or "./",
                            //    //     then remove that prefix from the input buffer; otherwise,
                            //    src += 2;
                            //    continue;
                            //}
                            //else if (ch1 == Dot && ch2 == Dot)
                            //{
                            //    // D.  if the input buffer consists only of "." or "..", then remove
                            //    //     that from the input buffer; otherwise,
                            //    src += 2;
                            //    continue;
                            //}

                            break;
                        case 3:
                            ch2 = *(src + 1);
                            ch3 = *(src + 2);

                            if (/*ch1 == Slash &&*/ ch2 == Dot && ch3 == Dot)
                            {
                                // C.  if the input buffer begins with a prefix of "/../" or "/..",
                                //     where ".." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer and remove the last
                                //     segment and its preceding "/" (if any) from the output
                                //     buffer; otherwise,
                                src += 2;
                                *src = Slash;

                                if (dst > start)
                                {
                                    do
                                    {
                                        dst--;
                                    } while (dst > start && *dst != Slash);
                                }

                                continue;
                            }
                            else if (/*ch1 == Slash &&*/ ch2 == Dot && ch3 == Slash)
                            {
                                // B.  if the input buffer begins with a prefix of "/./" or "/.",
                                //     where "." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer; otherwise,
                                src += 2;
                                continue;
                            }
                            //else if (ch1 == Dot && ch2 == Slash)
                            //{
                            //    // A.  If the input buffer begins with a prefix of "../" or "./",
                            //    //     then remove that prefix from the input buffer; otherwise,
                            //    src += 2;
                            //    continue;
                            //}
                            //else if (ch1 == Dot && ch2 == Dot && ch3 == Slash)
                            //{
                            //    // A.  If the input buffer begins with a prefix of "../" or "./",
                            //    //     then remove that prefix from the input buffer; otherwise,
                            //    src += 3;
                            //    continue;
                            //}

                            break;
                        default:
                            ch2 = *(src + 1);
                            ch3 = *(src + 2);
                            ch4 = *(src + 3);

                            if (/*ch1 == Slash &&*/ ch2 == Dot && ch3 == Dot && ch4 == Slash)
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
                                    } while (dst > start && *dst != Slash);
                                }

                                continue;
                            }
                            else if (/*ch1 == Slash &&*/ ch2 == Dot && ch3 == Slash)
                            {
                                // B.  if the input buffer begins with a prefix of "/./" or "/.",
                                //     where "." is a complete path segment, then replace that
                                //     prefix with "/" in the input buffer; otherwise,
                                src += 2;
                                continue;
                            }
                            //else if (ch1 == Dot && ch2 == Slash)
                            //{
                            //    // A.  If the input buffer begins with a prefix of "../" or "./",
                            //    //     then remove that prefix from the input buffer; otherwise,
                            //    src += 2;
                            //    continue;
                            //}
                            //else if (ch1 == Dot && ch2 == Dot && ch3 == Slash)
                            //{
                            //    // A.  If the input buffer begins with a prefix of "../" or "./",
                            //    //     then remove that prefix from the input buffer; otherwise,
                            //    src += 3;
                            //    continue;
                            //}

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
                    } while (src < end && ch1 != Slash);
                }

                if (dst == start)
                {
                    *dst = Slash;
                    return 1;
                }

                return (int)(dst - start);
            }
        }

        public static string RemoveDotSegments(string path)
        {
            if (ContainsDotSegments(path))
            {
                var normalizedChars = ArrayPool<char>.Shared.Rent(path.Length);
                var normalizedIndex = normalizedChars.Length;
                var pathIndex = path.Length - 1;
                var skipSegments = 0;

                while (pathIndex >= 0)
                {
                    if (pathIndex >= 2 && path[pathIndex] == '.' && path[pathIndex - 1] == '.' && path[pathIndex - 2] == '/')
                    {
                        if (normalizedIndex == normalizedChars.Length || normalizedChars[normalizedIndex] != '/')
                        {
                            normalizedChars[--normalizedIndex] = '/';
                        }

                        skipSegments++;
                        pathIndex -= 3;
                    }
                    else if (pathIndex >= 1 && path[pathIndex] == '.' && path[pathIndex - 1] == '/')
                    {
                        pathIndex -= 2;
                    }
                    else
                    {
                        while (pathIndex >= 0)
                        {
                            var lastChar = path[pathIndex];

                            if (skipSegments == 0)
                            {
                                normalizedChars[--normalizedIndex] = lastChar;
                            }

                            pathIndex--;

                            if (lastChar == '/')
                            {
                                break;
                            }
                        }

                        if (skipSegments > 0)
                        {
                            skipSegments--;
                        }
                    }
                }

                path = new string(normalizedChars, normalizedIndex, normalizedChars.Length - normalizedIndex);
                ArrayPool<char>.Shared.Return(normalizedChars);
            }

            return path;
        }

        private unsafe static bool ContainsDotSegments(string path)
        {
            fixed (char* ptr = path)
            {
                char* end = ptr + path.Length;

                for (char* p = ptr; p < end; p++)
                {
                    if (*p == '/')
                    {
                        p++;
                    }

                    if (p == end)
                    {
                        return false;
                    }

                    if (*p == '.')
                    {
                        p++;

                        if (p == end)
                        {
                            return true;
                        }

                        if (*p == '.')
                        {
                            p++;

                            if (p == end)
                            {
                                return true;
                            }

                            if (*p == '/')
                            {
                                return true;
                            }
                        }
                        else if (*p == '/')
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
