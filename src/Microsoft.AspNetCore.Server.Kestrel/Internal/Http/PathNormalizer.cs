// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public static class PathNormalizer
    {
        public static unsafe int RemoveDotSegments(Span<byte> span)
        {
            var length = span.Length;

            fixed (byte* start = &span.DangerousGetPinnableReference())
            {
                var end = start + length;
                var src = start;
                var dst = start;

                while (src < end)
                {
                    if (end - src >= 3 && *src == '.' && *(src + 1) == '.' && *(src + 2) == '/')
                    {
                        src += 3;
                    }
                    else if (end - src >= 2 && *src == '.' && *(src + 1) == '/')
                    {
                        src += 2;
                    }
                    else if (end - src >= 3 && *src == '/' && *(src + 1) == '.' && *(src + 2) == '/')
                    {
                        src += 2;
                    }
                    else if (end - src == 2 && *src == '/' && *(src + 1) == '.')
                    {
                        src += 2;
                    }
                    else if ((end - src >= 4 && *src == '/' && *(src + 1) == '.' && *(src + 2) == '.' && *(src + 3) == '/') ||
                             (end - src == 3 && *src == '/' && *(src + 1) == '.' && *(src + 2) == '.'))
                    {
                        src += 3;

                        if (dst > start)
                        {
                            do
                            {
                                dst--;
                            } while (dst > start && *dst != '/');
                        }
                    }
                    else if (end - src == 1 && *src == '.')
                    {
                        src += 1;
                    }
                    else if (end - src == 2 && *src == '.' && *(src + 1) == '.')
                    {
                        src += 2;
                    }
                    else
                    {
                        do
                        {
                            *dst++ = *src++;

                            if (src >= end || *src == '/')
                            {
                                break;
                            }
                        } while (src < end);
                    }
                }

                if (dst == start)
                {
                    *dst = (byte)'/';
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
