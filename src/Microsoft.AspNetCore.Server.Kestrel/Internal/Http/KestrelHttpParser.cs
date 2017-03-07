// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Numerics;
using System.Runtime.CompilerServices;
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

                            var endIndex = IndexOfAsciiNonNullCharaters(pBuffer, index, remaining, ByteLF);

                            int headerLineLength;
                            if (endIndex != -1)
                            {
                                headerLineLength = (endIndex - index + 1);
                                remaining -= headerLineLength;
                                TakeSingleHeader(handler, pBuffer + index, headerLineLength);
                            }
                            else
                            {
                                // Split buffers
                                headerLineLength = TakeSplitHeader(handler, reader.Cursor, ref buffer, ref bufferEnd, out var success);

                                if (!success)
                                {
                                    // Not there
                                    return false;
                                }
                            }

                            // Skip the reader forward past the header line
                            reader.Skip(headerLineLength);
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

        private static unsafe int TakeSplitHeader<T>(T handler, ReadCursor current, ref ReadableBuffer buffer, ref ReadCursor bufferEnd, out bool success) where T : IHttpHeadersHandler
        {
            // Split buffers
            if (ReadCursorOperations.Seek(current, bufferEnd, out var lineEnd, ByteLF) == -1)
            {
                // Not there
                success = false;
                return 0;
            }
            success = true;

            // Make sure LF is included in lineEnd
            lineEnd = buffer.Move(lineEnd, 1);
            var headerSpan = buffer.Slice(current, lineEnd).ToSpan();

            // We're going to the next span after this since we know we crossed spans here
            // so mark the remaining as equal to the headerSpan so that we end up at 0
            // on the next iteration

            var length = headerSpan.Length;
            fixed (byte* pHeader = &headerSpan.DangerousGetPinnableReference())
            {
                TakeSingleHeader<T>(handler, pHeader, length);
            }

            return length;
        }

        private static unsafe void TakeSingleHeader<T>(T handler, byte* pHeader, int headerLineLength) where T : IHttpHeadersHandler
        {
            var nameEnd = -1;
            var valueStart = -1;
            var valueEnd = -1;
            var nameHasWhitespace = false;
            var previouslyWhitespace = false;

            int i = 0;

            switch (HeaderState.Name)
            {
                case HeaderState.Name:
                    for (; i < headerLineLength; i++)
                    {
                        var ch = pHeader[i];
                        if (ch == ByteColon)
                        {
                            if (nameHasWhitespace)
                            {
                                RejectRequest(RequestRejectionReason.WhitespaceIsNotAllowedInHeaderName);
                            }
                            nameEnd = i;

                            // Consume space
                            i++;

                            goto case HeaderState.Whitespace;
                        }

                        if (ch == ByteSpace || ch == ByteTab)
                        {
                            nameHasWhitespace = true;
                        }
                    }
                    RejectRequest(RequestRejectionReason.NoColonCharacterFoundInHeaderLine);

                    break;
                case HeaderState.Whitespace:
                    for (; i < headerLineLength; i++)
                    {
                        var ch = pHeader[i];
                        var whitespace = ch == ByteTab || ch == ByteSpace || ch == ByteCR;

                        if (!whitespace)
                        {
                            // Mark the first non whitespace char as the start of the
                            // header value and change the state to expect to the header value
                            valueStart = i;

                            goto case HeaderState.ExpectValue;
                        }
                        // If we see a CR then jump to the next state directly
                        else if (ch == ByteCR)
                        {
                            goto case HeaderState.ExpectValue;
                        }
                    }

                    RejectRequest(RequestRejectionReason.MissingCRInHeaderLine);

                    break;
                case HeaderState.ExpectValue:
                    for (; i < headerLineLength; i++)
                    {
                        var ch = pHeader[i];
                        var whitespace = ch == ByteTab || ch == ByteSpace;

                        if (whitespace)
                        {
                            if (!previouslyWhitespace)
                            {
                                // If we see a whitespace char then maybe it's end of the
                                // header value
                                valueEnd = i;
                            }
                        }
                        else if (ch == ByteCR)
                        {
                            // If we see a CR and we haven't ever seen whitespace then
                            // this is the end of the header value
                            if (valueEnd == -1)
                            {
                                valueEnd = i;
                            }

                            // We never saw a non whitespace character before the CR
                            if (valueStart == -1)
                            {
                                valueStart = valueEnd;
                            }

                            // Consume space
                            i++;

                            goto case HeaderState.ExpectNewLine;
                        }
                        else
                        {
                            // If we find a non whitespace char that isn't CR then reset the end index
                            valueEnd = -1;
                        }

                        previouslyWhitespace = whitespace;
                    }
                    RejectRequest(RequestRejectionReason.MissingCRInHeaderLine);
                    break;
                case HeaderState.ExpectNewLine:
                    if (pHeader[i] != ByteLF)
                    {
                        RejectRequest(RequestRejectionReason.HeaderValueMustNotContainCR);
                    }
                    goto case HeaderState.Complete;
                case HeaderState.Complete:
                    break;
            }

            handler.OnHeader(new Span<byte>(pHeader, nameEnd), new Span<byte>(pHeader + valueStart, valueEnd - valueStart));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int IndexOfAsciiNonNullCharaters(byte* searchSpace, int start, int length, byte value)
        {
            var data = (sbyte*)searchSpace;
            sbyte ch = 0;
            var offset = start;
            var end = start + length;

            if (Vector.IsHardwareAccelerated)
            {
                // Check Vector lengths
                if (end - Vector<sbyte>.Count >= offset)
                {
                    Vector<sbyte> values = GetVector(value);
                    do
                    {
                        var vData = Unsafe.Read<Vector<sbyte>>(data + offset);
                        var vMatches = Vector.BitwiseOr(
                                            Vector.Equals(vData, values),
                                            Vector.LessThanOrEqual(vData, Vector<sbyte>.Zero));
                        if (!vMatches.Equals(Vector<sbyte>.Zero))
                        {
                            // Found match, reuse Vector values to keep register pressure low
                            values = vMatches;
                            break;
                        }

                        offset += Vector<sbyte>.Count;
                    } while (end - Vector<sbyte>.Count >= offset);

                    // Found match? Perform secondary search outside out of loop, so above loop body is small
                    if (end - Vector<sbyte>.Count >= offset)
                    {
                        // Find offset of first match
                        offset += LocateFirstFoundByte(values);
                        ch = data[offset];
                        // goto rather than inline return to keep function smaller
                        goto exit;
                    }
                }
            }

            // Haven't found match, scan through remaining
            for (; offset < end; offset++)
            {
                ch = data[offset];
                if (ch == value || ch <= 0)
                {
                    // goto rather than inline return to keep loop body small
                    goto exit;
                }
            }

            // No Matches
            return -1;
        exit:
            if (ch <= 0)
            {
                // Null char or Non-Ascii encountered, reject request
                RejectRequestHeadersContainNonAsciiOrNull();
            }
            return offset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector<sbyte> GetVector(byte vectorByte)
        {
            // Vector<sbyte> .ctor doesn't become an intrinsic due to detection issue
            // However this does cause it to become an intrinsic (with additional multiply and reg->reg copy)
            // https://github.com/dotnet/coreclr/issues/7459#issuecomment-253965670
            return Vector.AsVectorSByte(new Vector<uint>(vectorByte * 0x01010101u));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int LocateFirstFoundByte(Vector<sbyte> match)
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

        private const ulong xorPowerOfTwoToHighByte = (0x07ul |
                                                       0x06ul << 8 |
                                                       0x05ul << 16 |
                                                       0x04ul << 24 |
                                                       0x03ul << 32 |
                                                       0x02ul << 40 |
                                                       0x01ul << 48) + 1;

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

        public static void RejectRequestHeadersContainNonAsciiOrNull()
        {
            throw BadHttpRequestException.GetException(RequestRejectionReason.NonAsciiOrNullCharactersInHeader);
        }

        public static void RejectRequest(RequestRejectionReason reason)
        {
            throw BadHttpRequestException.GetException(reason);
        }

        public void RejectRequest(RequestRejectionReason reason, string value)
        {
            throw BadHttpRequestException.GetException(reason, value);
        }

        private void RejectRequestLine(Span<byte> span)
        {
            throw GetRejectRequestLineException(span);
        }

        private BadHttpRequestException GetRejectRequestLineException(Span<byte> span)
        {
            const int MaxRequestLineError = 32;
            return BadHttpRequestException.GetException(RequestRejectionReason.InvalidRequestLine,
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
