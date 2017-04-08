// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public class HttpParser : IHttpParser
    {
        public HttpParser(IKestrelTrace log)
        {
            Log = log;
        }

        private IKestrelTrace Log { get; }

        // byte types don't have a data type annotation so we pre-cast them; to avoid in-place casts
        private const byte ByteCR = (byte)'\r';
        private const byte ByteLF = (byte)'\n';
        private const byte ByteColon = (byte)':';
        private const byte ByteSpace = (byte)' ';
        private const byte ByteTab = (byte)'\t';
        private const byte ByteQuestionMark = (byte)'?';
        private const byte BytePercentage = (byte)'%';

        public bool ParseRequestLine(IHttpRequestLineHandler handler, ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined)
        {
            consumed = buffer.Start;
            examined = buffer.End;

            // Prepare the first span
            var span = buffer.First.Span;
            var lineIndex = span.IndexOf(ByteLF);
            if (lineIndex >= 0)
            {
                consumed = buffer.Move(consumed, lineIndex + 1);
                span = span.Slice(0, lineIndex + 1);
            }
            else if (buffer.IsSingleSpan)
            {
                // No request line end
                return false;
            }
            else if (TryGetNewLine(ref buffer, out var found))
            {
                span = buffer.Slice(consumed, found).ToSpan();
                consumed = found;
            }
            else
            {
                // No request line end
                return false;
            }

            // Fix and parse the span
            ParseRequestLine(handler, ref span.DangerousGetPinnableReference(), span.Length);

            examined = consumed;
            return true;
        }

        private void ParseRequestLine(IHttpRequestLineHandler handler, ref byte data, int length)
        {
            int offset;
            Span<byte> customMethod = default(Span<byte>);
            // Get Method and set the offset
            var method = HttpUtilities.GetKnownMethod(ref data, length, out offset);
            if (method == HttpMethod.Custom)
            {
                customMethod = GetUnknownMethod(ref data, length, out offset);
            }

            // Skip space
            offset++;

            byte ch = 0;
            // Target = Path and Query
            var pathEncoded = false;
            var pathStart = -1;
            for (; offset < length; offset++)
            {
                ch = Unsafe.Add(ref data, offset);
                if (ch == ByteSpace)
                {
                    if (pathStart == -1)
                    {
                        // Empty path is illegal
                        RejectRequestLine(ref data, length);
                    }

                    break;
                }
                else if (ch == ByteQuestionMark)
                {
                    if (pathStart == -1)
                    {
                        // Empty path is illegal
                        RejectRequestLine(ref data, length);
                    }

                    break;
                }
                else if (ch == BytePercentage)
                {
                    if (pathStart == -1)
                    {
                        // Path starting with % is illegal
                        RejectRequestLine(ref data, length);
                    }

                    pathEncoded = true;
                }
                else if (pathStart == -1)
                {
                    pathStart = offset;
                }
            }

            if (pathStart == -1)
            {
                // Start of path not found
                RejectRequestLine(ref data, length);
            }

            var pathBuffer = MakeSpan(ref Unsafe.Add(ref data, pathStart), offset - pathStart);

            // Query string
            var queryStart = offset;
            if (ch == ByteQuestionMark)
            {
                // We have a query string
                for (; offset < length; offset++)
                {
                    ch = Unsafe.Add(ref data, offset);
                    if (ch == ByteSpace)
                    {
                        break;
                    }
                }
            }

            // End of query string not found
            if (offset == length)
            {
                RejectRequestLine(ref data, length);
            }

            var targetBuffer = MakeSpan(ref Unsafe.Add(ref data, pathStart), offset - pathStart);
            var query = MakeSpan(ref Unsafe.Add(ref data, queryStart), offset - queryStart);

            // Consume space
            offset++;

            // Version
            var httpVersion = HttpUtilities.GetKnownVersion(ref Unsafe.Add(ref data, offset), length - offset);
            if (httpVersion == HttpVersion.Unknown)
            {
                if (Unsafe.Add(ref data, offset) == ByteCR || Unsafe.Add(ref data, length - 2) != ByteCR)
                {
                    // If missing delimiter or CR before LF, reject and log entire line
                    RejectRequestLine(ref data, length);
                }
                else
                {
                    // else inform HTTP version is unsupported.
                    RejectUnknownVersion(ref Unsafe.Add(ref data, offset), length - offset - 2);
                }
            }

            // After version's 8 bytes and CR, expect LF
            if (Unsafe.Add(ref data, offset + 8 + 1) != ByteLF)
            {
                RejectRequestLine(ref data, length);
            }

            handler.OnStartLine(method, httpVersion, targetBuffer, pathBuffer, query, customMethod, pathEncoded);
        }

        public bool ParseHeaders(IHttpHeadersHandler handler, ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined, out int consumedBytes)
        {
            consumed = buffer.Start;
            examined = buffer.End;
            consumedBytes = 0;

            var bufferEnd = buffer.End;

            var reader = new ReadableBufferReader(buffer);
            var start = default(ReadableBufferReader);
            var done = false;

            try
            {
                while (!reader.End)
                {
                    var span = reader.Span;
                    var remaining = span.Length - reader.Index;

                    ref byte pBuffer = ref span.DangerousGetPinnableReference();

                    while (remaining > 0)
                    {
                        var index = reader.Index;
                        int ch1;
                        int ch2;

                        // Fast path, we're still looking at the same span
                        if (remaining >= 2)
                        {
                            ch1 = Unsafe.Add(ref pBuffer, index);
                            ch2 = Unsafe.Add(ref pBuffer, index + 1);
                        }
                        else
                        {
                            // Store the reader before we look ahead 2 bytes (probably straddling
                            // spans)
                            start = reader;

                            // Possibly split across spans
                            ch1 = reader.Take();
                            ch2 = reader.Take();
                        }

                        if (ch1 == ByteCR)
                        {
                            // Check for final CRLF.
                            if (ch2 == -1)
                            {
                                // Reset the reader so we don't consume anything
                                reader = start;
                                return false;
                            }
                            else if (ch2 == ByteLF)
                            {
                                // If we got 2 bytes from the span directly so skip ahead 2 so that
                                // the reader's state matches what we expect
                                if (index == reader.Index)
                                {
                                    reader.Skip(2);
                                }

                                done = true;
                                return true;
                            }

                            // Headers don't end in CRLF line.
                            RejectRequest(RequestRejectionReason.InvalidRequestHeadersNoCRLF);
                        }

                        // We moved the reader so look ahead 2 bytes so reset both the reader
                        // and the index
                        if (index != reader.Index)
                        {
                            reader = start;
                            index = reader.Index;
                        }

                        var endIndex = MakeSpan(ref Unsafe.Add(ref pBuffer, index), remaining).IndexOf(ByteLF);
                        var length = 0;

                        if (endIndex != -1)
                        {
                            length = endIndex + 1;
                            ref byte pHeader = ref Unsafe.Add(ref pBuffer, index);

                            TakeSingleHeader(ref pHeader, length, handler);
                        }
                        else
                        {
                            var current = reader.Cursor;

                            // Split buffers
                            if (ReadCursorOperations.Seek(current, bufferEnd, out var lineEnd, ByteLF) == -1)
                            {
                                // Not there
                                return false;
                            }

                            // Make sure LF is included in lineEnd
                            lineEnd = buffer.Move(lineEnd, 1);
                            var headerSpan = buffer.Slice(current, lineEnd).ToSpan();
                            length = headerSpan.Length;

                            TakeSingleHeader(ref headerSpan.DangerousGetPinnableReference(), length, handler);

                            // We're going to the next span after this since we know we crossed spans here
                            // so mark the remaining as equal to the headerSpan so that we end up at 0
                            // on the next iteration
                            remaining = length;
                        }

                        // Skip the reader forward past the header line
                        reader.Skip(length);
                        remaining -= length;
                    }
                }

                return false;
            }
            finally
            {
                consumed = reader.Cursor;
                consumedBytes = reader.ConsumedBytes;

                if (done)
                {
                    examined = consumed;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindEndOfName(ref byte headerLine, int length)
        {
            var index = 0;
            var sawWhitespace = false;
            for (; index < length; index++)
            {
                var ch = Unsafe.Add(ref headerLine, index);
                if (ch == ByteColon)
                {
                    break;
                }
                if (ch == ByteTab || ch == ByteSpace || ch == ByteCR)
                {
                    sawWhitespace = true;
                }
            }

            if (index == length || sawWhitespace)
            {
                RejectRequestHeader(ref headerLine, length);
            }

            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TakeSingleHeader(ref byte headerLine, int length, IHttpHeadersHandler handler)
        {
            // Skip CR, LF from end position
            var valueEnd = length - 3;
            var nameEnd = FindEndOfName(ref headerLine, length);

            if (Unsafe.Add(ref headerLine, valueEnd + 2) != ByteLF)
            {
                RejectRequestHeader(ref headerLine, length);
            }
            if (Unsafe.Add(ref headerLine, valueEnd + 1) != ByteCR)
            {
                RejectRequestHeader(ref headerLine, length);
            }

            // Skip colon from value start
            var valueStart = nameEnd + 1;
            // Ignore start whitespace
            for (; valueStart < valueEnd; valueStart++)
            {
                var ch = Unsafe.Add(ref headerLine, valueStart);
                if (ch != ByteTab && ch != ByteSpace && ch != ByteCR)
                {
                    break;
                }
                else if (ch == ByteCR)
                {
                    RejectRequestHeader(ref headerLine, length);
                }
            }

            // Check for CR in value
            var i = valueStart + 1;
            if (Contains(ref Unsafe.Add(ref headerLine, i), valueEnd - i, ByteCR))
            {
                RejectRequestHeader(ref headerLine, length);
            }

            // Ignore end whitespace
            for (; valueEnd >= valueStart; valueEnd--)
            {
                var ch = Unsafe.Add(ref headerLine, valueEnd);
                if (ch != ByteTab && ch != ByteSpace)
                {
                    break;
                }
            }
            Span<byte> nameBuffer = MakeSpan(ref headerLine, nameEnd);
            var valueBuffer = MakeSpan(ref Unsafe.Add(ref headerLine, valueStart), valueEnd - valueStart + 1);

            handler.OnHeader(nameBuffer, valueBuffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe Span<byte> MakeSpan(ref byte data, int length)
        {
            // We can use this fast path on .NET Core 2.0 to make a span from ref byte
            // return Span<byte>.DangerousCreate(null, ref data, length);
            // For slow span, there's no way to do this because we don't have the backing object so we
            // need to fix the data
            fixed (byte* pointer = &data)
            {
                return new Span<byte>(pointer, length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool Contains(ref byte searchSpace, int length, byte value)
        {
            var i = 0;
            if (Vector.IsHardwareAccelerated)
            {
                // Check Vector lengths
                if (length - Vector<byte>.Count >= i)
                {
                    var vValue = GetVector(value);

                    do
                    {
                        if (!Vector<byte>.Zero.Equals(Vector.Equals(vValue, Unsafe.ReadUnaligned<Vector<byte>>(ref Unsafe.Add(ref searchSpace, i)))))
                        {
                            goto found;
                        }

                        i += Vector<byte>.Count;
                    } while (length - Vector<byte>.Count >= i);
                }
            }

            // Check remaining for CR
            for (; i <= length; i++)
            {
                var ch = Unsafe.Add(ref searchSpace, i);
                if (ch == value)
                {
                    goto found;
                }
            }
            return false;
            found:
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryGetNewLine(ref ReadableBuffer buffer, out ReadCursor found)
        {
            var start = buffer.Start;
            if (ReadCursorOperations.Seek(start, buffer.End, out found, ByteLF) != -1)
            {
                // Move 1 byte past the \n
                found = buffer.Move(found, 1);
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Span<byte> GetUnknownMethod(ref byte data, int length, out int methodLength)
        {
            methodLength = 0;
            for (var i = 0; i < length; i++)
            {
                var ch = Unsafe.Add(ref data, i);

                if (ch == ByteSpace)
                {
                    if (i == 0)
                    {
                        RejectRequestLine(ref data, length);
                    }

                    methodLength = i;
                    break;
                }
                else if (!IsValidTokenChar((char)ch))
                {
                    RejectRequestLine(ref data, length);
                }
            }

            return MakeSpan(ref data, length);
        }

        private static bool IsValidTokenChar(char c)
        {
            // Determines if a character is valid as a 'token' as defined in the
            // HTTP spec: https://tools.ietf.org/html/rfc7230#section-3.2.6
            return
                (c >= '0' && c <= '9') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                c == '!' ||
                c == '#' ||
                c == '$' ||
                c == '%' ||
                c == '&' ||
                c == '\'' ||
                c == '*' ||
                c == '+' ||
                c == '-' ||
                c == '.' ||
                c == '^' ||
                c == '_' ||
                c == '`' ||
                c == '|' ||
                c == '~';
        }

        private void RejectRequest(RequestRejectionReason reason)
            => throw BadHttpRequestException.GetException(reason);

        private void RejectRequestLine(ref byte requestLine, int length)
            => throw GetInvalidRequestException(RequestRejectionReason.InvalidRequestLine, ref requestLine, length);

        private void RejectRequestHeader(ref byte headerLine, int length)
            => throw GetInvalidRequestException(RequestRejectionReason.InvalidRequestHeader, ref headerLine, length);

        private void RejectUnknownVersion(ref byte version, int length)
            => throw GetInvalidRequestException(RequestRejectionReason.UnrecognizedHTTPVersion, ref version, length);

        private BadHttpRequestException GetInvalidRequestException(RequestRejectionReason reason, ref byte detail, int length)
            => BadHttpRequestException.GetException(
                  reason,
                  Log.IsEnabled(LogLevel.Information)
                      ? MakeSpan(ref detail, length).GetAsciiStringEscaped(Constants.MaxExceptionDetailSize)
                      : string.Empty);


        public void Reset()
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<byte> GetVector(byte vectorByte)
        {
            // Vector<byte> .ctor doesn't become an intrinsic due to detection issue
            // However this does cause it to become an intrinsic (with additional multiply and reg->reg copy)
            // https://github.com/dotnet/coreclr/issues/7459#issuecomment-253965670
            return Vector.AsVectorByte(new Vector<uint>(vectorByte * 0x01010101u));
        }
    }
}
