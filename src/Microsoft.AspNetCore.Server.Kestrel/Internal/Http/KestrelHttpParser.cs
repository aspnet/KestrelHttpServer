// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class KestrelHttpParser : IHttpParser
    {
        public KestrelHttpParser(IKestrelTrace log)
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

            ReadCursor end;
            Span<byte> span;

            // If the buffer is a single span then use it to find the LF
            if (buffer.IsSingleSpan)
            {
                var startLineSpan = buffer.First.Span;
                var lineIndex = startLineSpan.IndexOfVectorized(ByteLF);

                if (lineIndex == -1)
                {
                    return false;
                }

                end = buffer.Move(consumed, lineIndex + 1);
                span = startLineSpan.Slice(0, lineIndex + 1);
            }
            else
            {
                var start = buffer.Start;
                if (ReadCursorOperations.Seek(start, buffer.End, out end, ByteLF) == -1)
                {
                    return false;
                }

                // Move 1 byte past the \n
                end = buffer.Move(end, 1);
                var startLineBuffer = buffer.Slice(start, end);

                span = startLineBuffer.ToSpan();
            }

            var pathStart = -1;
            var queryStart = -1;
            var queryEnd = -1;
            var pathEnd = -1;
            var versionStart = -1;

            var httpVersion = HttpVersion.Unknown;
            HttpMethod method;
            Span<byte> customMethod;
            var i = 0;
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

            var bufferEnd = buffer.End;

            var reader = new ReadableBufferReader(buffer);
            var start = default(ReadableBufferReader);
            var done = false;

            try
            {
                while (!reader.End)
                {
                    var span = reader.Span;
                    var remaining = span.Length;

                    fixed (byte* pBuffer = &span.DangerousGetPinnableReference())
                    {
                        while (remaining > 0)
                        {
                            var index = reader.Index;
                            int ch1;
                            int ch2;

                            // Fast path, we're still looking at the same span
                            if (remaining >= 2)
                            {
                                ch1 = pBuffer[index];
                                ch2 = pBuffer[index + 1];
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
                                RejectRequest(RequestRejectionReason.HeadersCorruptedInvalidHeaderSequence);
                            }
                            else if (ch1 == ByteSpace || ch1 == ByteTab)
                            {
                                RejectRequest(RequestRejectionReason.HeaderLineMustNotStartWithWhitespace);
                            }

                            // We moved the reader so look ahead 2 bytes so reset both the reader
                            // and the index
                            if (index != reader.Index)
                            {
                                reader = start;
                                index = reader.Index;
                            }

                            var endIndex = IndexOf(pBuffer, index, remaining, ByteLF);
                            var length = 0;

                            if (endIndex != -1)
                            {
                                length = (endIndex - index + 1);
                                var pHeader = pBuffer + index;

                                if (!TakeSingleHeader(pHeader, length, out var nameStart, out var nameEnd, out var valueStart, out var valueEnd))
                                {
                                    return false;
                                }

                                var nameBuffer = new Span<byte>(pHeader + nameStart, nameEnd - nameStart);
                                var valueBuffer = new Span<byte>(pHeader + valueStart, valueEnd - valueStart);

                                handler.OnHeader(nameBuffer, valueBuffer);
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

                                fixed (byte* pHeader = &headerSpan.DangerousGetPinnableReference())
                                {
                                    if (!TakeSingleHeader(pHeader, headerSpan.Length, out var nameStart, out var nameEnd, out var valueStart, out var valueEnd))
                                    {
                                        return false;
                                    }

                                    var nameBuffer = new Span<byte>(pHeader + nameStart, nameEnd - nameStart);
                                    var valueBuffer = new Span<byte>(pHeader + valueStart, valueEnd - valueStart);

                                    handler.OnHeader(nameBuffer, valueBuffer);
                                }

                                // We're going to the next span after this since we know we crossed spans here
                                // so mark the remaining as equal to the headerSpan so that we end up at 0
                                // on the next iteration
                                remaining = headerSpan.Length;
                                length = headerSpan.Length;
                            }

                            // Skip the reader forward past the header line
                            reader.Skip(length);
                            remaining -= length;
                        }
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

        private unsafe int IndexOf(byte* pBuffer, int index, int length, byte value)
        {
            var pCurrent = pBuffer + index;
            var pEnd = pBuffer + index + length;

            while (pCurrent < pEnd)
            {
                var ch = *pCurrent;
                if (ch == value)
                {
                    return index;
                }
                pCurrent++;
                index++;
            }
            return -1;
        }

        private unsafe int IndexOfAny(byte* pBuffer, int index, int length, byte value, byte value1)
        {
            var pCurrent = pBuffer + index;
            var pEnd = pBuffer + index + length;

            while (pCurrent < pEnd)
            {
                var ch = *pCurrent;
                if (ch == value || ch == value1)
                {
                    return index;
                }
                pCurrent++;
                index++;
            }

            return -1;
        }

        private unsafe bool TakeSingleHeader(byte* pHeader, int headerLineLength, out int nameStart, out int nameEnd, out int valueStart, out int valueEnd)
        {
            nameStart = 0;
            nameEnd = -1;
            valueStart = -1;
            valueEnd = -1;
            var index = 0;

            var pCurrent = pHeader + index;
            var pEnd = pHeader + headerLineLength;

            nameEnd = IndexOf(pHeader, index, headerLineLength, ByteColon);

            if (nameEnd == -1)
            {
                RejectRequest(RequestRejectionReason.NoColonCharacterFoundInHeaderLine);
            }

            if (IndexOfAny(pHeader, index, nameEnd, ByteTab, ByteSpace) != -1)
            {
                RejectRequest(RequestRejectionReason.WhitespaceIsNotAllowedInHeaderName);
            }

            // Skip colon
            index += nameEnd + 1;
            pCurrent += index;
            valueStart = index;
            var pValueStart = pCurrent;

            while (pCurrent < pEnd)
            {
                var ch = *pCurrent;
                if (ch != ByteTab && ch != ByteSpace && ch != ByteCR)
                {
                    valueStart = index;
                    pValueStart = pCurrent;
                    break;
                }
                else if (ch == ByteCR)
                {
                    break;
                }
                pCurrent++;
                index++;
            }

            var endIndex = headerLineLength - 1;
            pEnd--;

            if (*pEnd != ByteLF)
            {
                RejectRequest(RequestRejectionReason.HeaderValueMustNotContainCR);
            }

            endIndex--;
            pEnd--;

            if (*pEnd != ByteCR)
            {
                RejectRequest(RequestRejectionReason.MissingCRInHeaderLine);
            }

            endIndex--;
            pEnd--;

            while (pEnd >= pValueStart)
            {
                var ch = *pEnd;
                if (ch != ByteTab && ch != ByteSpace && ch != ByteCR && valueEnd == -1)
                {
                    valueEnd = endIndex;
                }
                else if (ch == ByteCR)
                {
                    RejectRequest(RequestRejectionReason.HeaderValueMustNotContainCR);
                }

                pEnd--;
                endIndex--;
            }

            if (valueEnd == -1)
            {
                valueEnd = valueStart;
            }
            else
            {
                valueEnd++;
            }

            return true;
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
            throw BadHttpRequestException.GetException(reason);
        }

        public void RejectRequest(RequestRejectionReason reason, string value)
        {
            throw BadHttpRequestException.GetException(reason, value);
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

        private enum HeaderState
        {
            Name,
            Whitespace,
            ExpectValue,
            ExpectNewLine,
            Complete
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
    }
}
