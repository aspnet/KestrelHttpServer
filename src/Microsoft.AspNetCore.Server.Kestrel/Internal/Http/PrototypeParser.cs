// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Utf8;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class PrototypeParser : IHttpParser
    {
        public PrototypeParser(IKestrelTrace log)
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

        public unsafe bool ParseRequestLine<T>(T handler, ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined) where T : IHttpRequestLineHandler
        {
            consumed = buffer.Start;
            examined = buffer.End;

            var start = buffer.Start;
            if (ReadCursorOperations.Seek(start, buffer.End, out var end, ByteLF) == -1)
            {
                return false;
            }

            // Move 1 byte past the \n
            end = buffer.Move(end, 1);
            var startLineBuffer = buffer.Slice(start, end);

            Span<byte> span;
            if (startLineBuffer.IsSingleSpan)
            {
                // No copies, directly use the one and only span
                span = startLineBuffer.First.Span;
            }
            else
            {
                // We're not a single span here but we can use pooled arrays to avoid allocations in the rare case
                span = new Span<byte>(new byte[startLineBuffer.Length]);
                startLineBuffer.CopyTo(span);
            }

            var pathStart = -1;
            var queryStart = -1;
            var queryEnd = -1;
            var pathEnd = -1;
            var versionStart = -1;

            HttpVersion httpVersion = HttpVersion.Unknown;
            HttpMethod method;
            Span<byte> customMethod;
            int i = 0;
            var length = span.Length;
            var done = false;

            fixed (byte* data = &span.DangerousGetPinnableReference())
            {
                switch (StartLineState.KnownMethod)
                {
                    case StartLineState.KnownMethod:
                        if (span.GetKnownMethod(out method, out var methodLength))
                        {
                            // Update the index, current char, state and jump directly
                            // to the next state
                            i += methodLength + 1;

                            goto case StartLineState.Path;
                        }
                        goto case StartLineState.UnknownMethod;

                    case StartLineState.UnknownMethod:
                        for (; i < length; i++)
                        {
                            var ch = data[i];

                            if (ch == ByteSpace)
                            {
                                customMethod = span.Slice(0, i);

                                if (customMethod.Length == 0)
                                {
                                    RejectRequestLine(span);
                                }
                                // Consume space
                                i++;

                                goto case StartLineState.Path;
                            }

                            if (!IsValidTokenChar((char)ch))
                            {
                                RejectRequestLine(span);
                            }
                        }

                        break;
                    case StartLineState.Path:
                        for (; i < length; i++)
                        {
                            var ch = data[i];
                            if (ch == ByteSpace)
                            {
                                pathEnd = i;

                                if (pathStart == -1)
                                {
                                    // Empty path is illegal
                                    RejectRequestLine(span);
                                }

                                // No query string found
                                queryStart = queryEnd = i;

                                // Consume space
                                i++;

                                goto case StartLineState.KnownVersion;
                            }
                            else if (ch == ByteQuestionMark)
                            {
                                pathEnd = i;

                                if (pathStart == -1)
                                {
                                    // Empty path is illegal
                                    RejectRequestLine(span);
                                }

                                queryStart = i;
                                goto case StartLineState.QueryString;
                            }
                            else if (ch == BytePercentage)
                            {
                                if (pathStart == -1)
                                {
                                    RejectRequestLine(span);
                                }
                            }

                            if (pathStart == -1)
                            {
                                pathStart = i;
                            }
                        }
                        break;
                    case StartLineState.QueryString:
                        for (; i < length; i++)
                        {
                            var ch = data[i];
                            if (ch == ByteSpace)
                            {
                                queryEnd = i;

                                // Consume space
                                i++;

                                goto case StartLineState.KnownVersion;
                            }
                        }
                        break;
                    case StartLineState.KnownVersion:
                        // REVIEW: We don't *need* to slice here but it makes the API
                        // nicer, slicing should be free :)
                        if (span.Slice(i).GetKnownVersion(out httpVersion, out var versionLenght))
                        {
                            // Update the index, current char, state and jump directly
                            // to the next state
                            i += versionLenght + 1;
                            goto case StartLineState.NewLine;
                        }

                        versionStart = i;

                        goto case StartLineState.UnknownVersion;

                    case StartLineState.UnknownVersion:
                        for (; i < length; i++)
                        {
                            var ch = data[i];
                            if (ch == ByteCR)
                            {
                                var versionSpan = span.Slice(versionStart, i - versionStart);

                                if (versionSpan.Length == 0)
                                {
                                    RejectRequestLine(span);
                                }
                                else
                                {
                                    RejectRequest(RequestRejectionReason.UnrecognizedHTTPVersion,
                                        versionSpan.GetAsciiStringEscaped(32));
                                }
                            }
                        }
                        break;
                    case StartLineState.NewLine:
                        if (data[i] != ByteLF)
                        {
                            RejectRequestLine(span);
                        }
                        i++;

                        goto case StartLineState.Complete;
                    case StartLineState.Complete:
                        done = true;
                        break;
                }
            }

            if (!done)
            {
                RejectRequestLine(span);
            }

            var pathBuffer = span.Slice(pathStart, pathEnd - pathStart);
            var targetBuffer = span.Slice(pathStart, queryEnd - pathStart);
            var query = span.Slice(queryStart, queryEnd - queryStart);

            handler.OnStartLine(method, httpVersion, targetBuffer, pathBuffer, query, customMethod);

            consumed = end;
            examined = consumed;
            return true;
        }

        public unsafe bool ParseHeaders<T>(T handler, ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined, out int consumedBytes) where T : IHttpHeadersHandler
        {
            consumed = buffer.Start;
            examined = buffer.End;
            consumedBytes = 0;

            ReadableBuffer bufferLeft = buffer;
            ReadCursor headerStart = buffer.Start;
            int headerLength = 0;
            bool carryLfCheck = false;
            bool splitSpan = false;
            bool first = true;

            while (true)
            {
                if (bufferLeft.Start == buffer.End)
                {
                    consumed = headerStart;
                    return false;
                }

                Span<byte> span = bufferLeft.First.Span;
                fixed (byte* pSpan = &span.DangerousGetPinnableReference())
                {
                    var searchSpace = pSpan;
                    int bytesLeft = span.Length;

                    if (first)
                    {
                        if (*searchSpace == ByteSpace || *searchSpace == ByteTab)
                            RejectRequest(RequestRejectionReason.HeaderLineMustNotStartWithWhitespace);

                        first = false;
                    }

                    if (carryLfCheck)
                    {
                        if (*searchSpace != ByteLF)
                            RejectRequest(RequestRejectionReason.HeadersCorruptedInvalidHeaderSequence);

                        searchSpace += 1;
                        bytesLeft -= 1;
                        carryLfCheck = false;
                        headerStart = bufferLeft.Move(headerStart, 1);
                    }

                    while (true)
                    {
                        // TODO: Is the next 2 characters CR, LF. If they are, we are done with the headers.
                        if (!splitSpan && *searchSpace == ByteCR)
                        {
                            if (bytesLeft > 1)
                            {
                                if (*(searchSpace + 1) != ByteLF)
                                    RejectRequest(RequestRejectionReason.HeadersCorruptedInvalidHeaderSequence);

                                consumed = examined = bufferLeft.Move(headerStart, 2);
                                consumedBytes += 2;
                                return true;
                            }

                            // We are missing the last LF
                            consumed = headerStart;
                            return false;
                        }

                        int offset = FindNextHeaderEnd(searchSpace, bytesLeft);
                        if (offset == -1)
                        {
                            headerLength += bytesLeft;
                            var sliceCursor = bufferLeft.Move(headerStart, headerLength);
                            bufferLeft = bufferLeft.Slice(sliceCursor);
                            splitSpan = true;
                            break;
                        }

                        headerLength += offset;

                        // We have a header line
                        if (!splitSpan)
                        {
                            ProcessHeader(handler, searchSpace, headerLength);
                        }
                        else
                        {
                            ReadableBuffer headerBuffer = bufferLeft.Slice(headerStart, headerLength);
                            ProcessHeaderSlow(handler, headerBuffer);
                            splitSpan = false;
                        }

                        if (offset == bytesLeft - 1)
                        {
                            // If we are at the end of the current span, delay the LF check for the next span.
                            carryLfCheck = true;
                            offset++;
                            headerLength++;
                        }
                        else
                        {
                            if (*(searchSpace + offset + 1) != ByteLF)
                                RejectRequest(RequestRejectionReason.HeadersCorruptedInvalidHeaderSequence);

                            offset += 2;
                            headerLength += 2;
                            searchSpace += offset;
                        }

                        bytesLeft -= offset;
                        consumedBytes += headerLength;
                        headerStart = bufferLeft.Move(headerStart, headerLength);
                        headerLength = 0;

                        if (bytesLeft == 0)
                        {
                            bufferLeft = bufferLeft.Slice(headerStart);
                            break;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void ProcessHeader(IHttpHeadersHandler handler, byte* pHeader, int length)
        {
            int nameEnd = IndexOf(pHeader, 0, length, ByteColon);
            if (nameEnd == -1)
                RejectRequest(RequestRejectionReason.NoColonCharacterFoundInHeaderLine);

            int valueStart = nameEnd + 1;
            // Skip whitespace
            for (; valueStart < length; valueStart++)
            {
                byte ch = *(pHeader + valueStart);
                if (ch != ByteTab && ch != ByteSpace)
                    break;
            }

            int valueLen = length - valueStart;

            byte* pChar = pHeader + valueStart + valueLen - 1;
            for (; valueLen > 0 && (*pChar == ByteTab || *pChar == ByteSpace); pChar--, valueLen--);

            handler.OnHeader(new Span<byte>(pHeader, nameEnd), new Span<byte>(pHeader + valueStart, valueLen));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe int IndexOf(byte* data, int index, int length, byte value)
        {
            for (int i = index; i < length; i++)
            {
                if (data[i] == value)
                {
                    return i;
                }
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe  void ProcessHeaderSlow(IHttpHeadersHandler handler, ReadableBuffer headerBuffer)
        {
            const int stackAllocLimit = 512;

            Span<byte> headerSpan;
            if (headerBuffer.IsSingleSpan)
            {
                // No copies, directly use the one and only span
                headerSpan = headerBuffer.ToSpan();
                //Console.WriteLine($"\r\n************ NEW HEADER (single) ************* => <{new Utf8String(headerSpan)}>\r\n");
            }
            else if (headerBuffer.Length < stackAllocLimit)
            {
                unsafe
                {
                    // Multiple buffers and < stackAllocLimit, copy into a stack buffer
                    byte* stackBuffer = stackalloc byte[headerBuffer.Length];
                    headerSpan = new Span<byte>(stackBuffer, headerBuffer.Length);
                    headerBuffer.CopyTo(headerSpan);
                }

                //Console.WriteLine($"\r\n************ NEW HEADER (copy small) ************* => <{new Utf8String(headerSpan)}>\r\n");
            }
            else
            {
                // We're not a single span here but we can use pooled arrays to avoid allocations in the rare case
                headerSpan = new Span<byte>(new byte[headerBuffer.Length]);
                headerBuffer.CopyTo(headerSpan);
                //Console.WriteLine($"\r\n************ NEW HEADER (copy big) ************* => <{new Utf8String(headerSpan)}>\r\n");
            }

            fixed (byte* pHeader = &headerSpan.DangerousGetPinnableReference())
            {
                ProcessHeader(handler, pHeader, headerSpan.Length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe int FindNextHeaderEnd(byte * pBuffer, int length)
        {
            var offset = 0;

            Vector<byte> values = new Vector<byte>(ByteCR);
            while (length - Vector<byte>.Count >= offset)
            {
                var vFlaggedMatches = Vector.Equals(Unsafe.Read<Vector<byte>>(pBuffer + offset), values);
                if (!vFlaggedMatches.Equals(Vector<byte>.Zero))
                {
                    values = vFlaggedMatches;
                    break;
                }

                offset += Vector<byte>.Count;
            }

            // Found match?
            if (length - Vector<byte>.Count >= offset)
            {
                offset += LocateFirstFoundByte(values);
                goto foundMatch;
            }

            // Haven't found match, scan through remaining
            for (; offset < length; offset++)
            {
                if (*(pBuffer + offset) == ByteCR)
                {
                    // goto rather than inline return to keep loop body small
                    goto foundMatch;
                }
            }

            // No match
            return -1;

        foundMatch:;
            return offset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int LocateFirstFoundByte(Vector<byte> match)
        {
            var vector64 = Vector.AsVectorUInt64(match);
            ulong candidate = 0;
            var i = 0;
            // Pattern unrolled by jit https://github.com/dotnet/coreclr/pull/8001
            for (; i < Vector<ulong>.Count; i++)
            {
                candidate = vector64[i];
                if (candidate != 0)
                {
                    break;
                }
            }

            // Single LEA instruction with jitted const (using function result)
            return i * 8 + LocateFirstFoundByte(candidate);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int LocateFirstFoundByte(ulong match)
        {
            unchecked
            {
                // Flag least significant power of two bit
                var powerOfTwoFlag = match ^ (match - 1);
                // Shift all powers of two into the high byte and extract
                return (int)((powerOfTwoFlag * xorPowerOfTwoToHighByte) >> 57);
            }
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

        public void RejectRequest(RequestRejectionReason reason)
        {
            RejectRequest(BadHttpRequestException.GetException(reason));
        }

        public void RejectRequest(RequestRejectionReason reason, string value)
        {
            RejectRequest(BadHttpRequestException.GetException(reason, value));
        }

        private void RejectRequest(BadHttpRequestException ex)
        {
            throw ex;
        }

        private void RejectRequestLine(Span<byte> span)
        {
            const int MaxRequestLineError = 32;
            RejectRequest(RequestRejectionReason.InvalidRequestLine,
                Log.IsEnabled(LogLevel.Information) ? span.GetAsciiStringEscaped(MaxRequestLineError) : string.Empty);
        }

        public void Reset()
        {
        }

        private enum StartLineState
        {
            KnownMethod,
            UnknownMethod,
            Path,
            QueryString,
            KnownVersion,
            UnknownVersion,
            NewLine,
            Complete
        }

        private const ulong xorPowerOfTwoToHighByte = (0x07ul |
                                                       0x06ul << 8 |
                                                       0x05ul << 16 |
                                                       0x04ul << 24 |
                                                       0x03ul << 32 |
                                                       0x02ul << 40 |
                                                       0x01ul << 48) + 1;
    }
}