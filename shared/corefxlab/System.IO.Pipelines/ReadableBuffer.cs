// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Collections.Sequences;
using System.Text;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    /// <summary>
    /// Represents a buffer that can read a sequential series of bytes.
    /// </summary>
    public struct ReadableBuffer : ISequence<ReadOnlyBuffer<byte>>
    {
        internal ReadCursor BufferStart;
        internal ReadCursor BufferEnd;
        internal int BufferLength;

        /// <summary>
        /// Length of the <see cref="ReadableBuffer"/> in bytes.
        /// </summary>
        public int Length => BufferLength;

        /// <summary>
        /// Determines if the <see cref="ReadableBuffer"/> is empty.
        /// </summary>
        public bool IsEmpty => BufferLength == 0;

        /// <summary>
        /// Determins if the <see cref="ReadableBuffer"/> is a single <see cref="Buffer{Byte}"/>.
        /// </summary>
        public bool IsSingleSpan => BufferStart.Segment == BufferEnd.Segment;

        public Buffer<byte> First
        {
            get
            {
                BufferStart.TryGetBuffer(BufferEnd, out Buffer<byte> first);
                return first;
            }
        }

        /// <summary>
        /// A cursor to the start of the <see cref="ReadableBuffer"/>.
        /// </summary>
        public ReadCursor Start => BufferStart;

        /// <summary>
        /// A cursor to the end of the <see cref="ReadableBuffer"/>
        /// </summary>
        public ReadCursor End => BufferEnd;

        internal ReadableBuffer(ReadCursor start, ReadCursor end)
        {
            BufferStart = start;
            BufferEnd = end;
            BufferLength = start.GetLength(end);
        }

        private ReadableBuffer(ref ReadableBuffer buffer)
        {
            var begin = buffer.BufferStart;
            var end = buffer.BufferEnd;

            BufferSegment segmentTail;
            var segmentHead = BufferSegment.Clone(begin, end, out segmentTail);

            begin = new ReadCursor(segmentHead);
            end = new ReadCursor(segmentTail, segmentTail.End);

            BufferStart = begin;
            BufferEnd = end;

            BufferLength = buffer.BufferLength;
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadableBuffer"/>, beginning at 'start', and is at most length bytes
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="length">The length of the slice</param>
        public ReadableBuffer Slice(int start, int length)
        {
            var begin = BufferStart.Seek(start, BufferEnd, false);
            var end = begin.Seek(length, BufferEnd, false);
            return Slice(begin, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadableBuffer"/>, beginning at 'start', ending at 'end' (inclusive).
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="end">The end (inclusive) of the slice</param>
        public ReadableBuffer Slice(int start, ReadCursor end)
        {
            BufferEnd.BoundsCheck(end);
            var begin = BufferStart.Seek(start, end);
            return Slice(begin, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadableBuffer"/>, beginning at 'start', ending at 'end' (inclusive).
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="ReadCursor"/> at which to begin this slice.</param>
        /// <param name="end">The ending (inclusive) <see cref="ReadCursor"/> of the slice</param>
        public ReadableBuffer Slice(ReadCursor start, ReadCursor end)
        {
            BufferEnd.BoundsCheck(end);
            end.BoundsCheck(start);

            return new ReadableBuffer(start, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadableBuffer"/>, beginning at 'start', and is at most length bytes
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="ReadCursor"/> at which to begin this slice.</param>
        /// <param name="length">The length of the slice</param>
        public ReadableBuffer Slice(ReadCursor start, int length)
        {
            BufferEnd.BoundsCheck(start);

            var end = start.Seek(length, BufferEnd, false);

            return Slice(start, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadableBuffer"/>, beginning at 'start', ending at the existing <see cref="ReadableBuffer"/>'s end.
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="ReadCursor"/> at which to begin this slice.</param>
        public ReadableBuffer Slice(ReadCursor start)
        {
            BufferEnd.BoundsCheck(start);

            return new ReadableBuffer(start, BufferEnd);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadableBuffer"/>, beginning at 'start', ending at the existing <see cref="ReadableBuffer"/>'s end.
        /// </summary>
        /// <param name="start">The start index at which to begin this slice.</param>
        public ReadableBuffer Slice(int start)
        {
            if (start == 0) return this;

            var begin = BufferStart.Seek(start, BufferEnd, false);
            return new ReadableBuffer(begin, BufferEnd);
        }

        /// <summary>
        /// This transfers ownership of the buffer from the <see cref="IPipeReader"/> to the caller of this method. Preserved buffers must be disposed to avoid
        /// memory leaks.
        /// </summary>
        public PreservedBuffer Preserve()
        {
            var buffer = new ReadableBuffer(ref this);
            return new PreservedBuffer(ref buffer);
        }

        /// <summary>
        /// Copy the <see cref="ReadableBuffer"/> to the specified <see cref="Span{Byte}"/>.
        /// </summary>
        /// <param name="destination">The destination <see cref="Span{Byte}"/>.</param>
        public void CopyTo(Span<byte> destination)
        {
            if (Length > destination.Length)
            {
                PipelinesThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.destination);
            }

            foreach (var buffer in this)
            {
                buffer.Span.CopyTo(destination);
                destination = destination.Slice(buffer.Length);
            }
        }

        /// <summary>
        /// Converts the <see cref="ReadableBuffer"/> to a <see cref="T:byte[]"/>
        /// </summary>
        public byte[] ToArray()
        {
            var buffer = new byte[Length];
            CopyTo(buffer);
            return buffer;
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var buffer in this)
            {
                SpanExtensions.AppendAsLiteral(buffer.Span, sb);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns an enumerator over the <see cref="ReadableBuffer"/>
        /// </summary>
        public BufferEnumerator GetEnumerator()
        {
            return new BufferEnumerator(BufferStart, BufferEnd);
        }

        internal void ClearCursors()
        {
            BufferStart = default(ReadCursor);
            BufferEnd = default(ReadCursor);
        }

        /// <summary>
        /// Create a <see cref="ReadableBuffer"/> over an array.
        /// </summary>
        public static ReadableBuffer Create(byte[] data)
        {
            if (data == null)
            {
                PipelinesThrowHelper.ThrowArgumentNullException(ExceptionArgument.data);
            }

            OwnedBuffer<byte> buffer = data;
            return CreateInternal(buffer, 0, data.Length);
        }

        /// <summary>
        /// Create a <see cref="ReadableBuffer"/> over an array.
        /// </summary>
        public static ReadableBuffer Create(byte[] data, int offset, int length)
        {
            if (data == null)
            {
                PipelinesThrowHelper.ThrowArgumentNullException(ExceptionArgument.data);
            }

            OwnedBuffer<byte> buffer = data;
            return Create(buffer, offset, length);
        }

        /// <summary>
        /// Create a <see cref="ReadableBuffer"/> over an OwnedBuffer.
        /// </summary>
        public static ReadableBuffer Create(OwnedBuffer<byte> data, int offset, int length)
        {
            if (data == null)
            {
                PipelinesThrowHelper.ThrowArgumentNullException(ExceptionArgument.data);
            }

            if (offset < 0)
            {
                PipelinesThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.offset);
            }

            if (length < 0 || length > data.Length - offset)
            {
                PipelinesThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.length);
            }

            return CreateInternal(data, offset, length);
        }

        private static ReadableBuffer CreateInternal(OwnedBuffer<byte> data, int offset, int length)
        {
            var segment = new BufferSegment(data);
            segment.Start = offset;
            segment.End = offset + length;
            return new ReadableBuffer(new ReadCursor(segment, offset), new ReadCursor(segment, offset + length));
        }

        bool ISequence<ReadOnlyBuffer<byte>>.TryGet(ref Position position, out ReadOnlyBuffer<byte> item, bool advance)
        {
            if (position == Position.First)
            {
                // First is already sliced
                item = First;
                if (advance)
                {
                    if (BufferStart.IsEnd)
                    {
                        position = Position.AfterLast;
                    }
                    else
                    {
                        position.ObjectPosition = BufferStart.Segment.Next;
                        if (position.ObjectPosition == null)
                        {
                            position = Position.AfterLast;
                        }
                    }
                }
                return true;
            }
            else if (position == Position.AfterLast)
            {
                item = default(ReadOnlyBuffer<byte>);
                return false;
            }

            var currentSegment = (BufferSegment)position.ObjectPosition;
            if (advance)
            {
                position.ObjectPosition = currentSegment.Next;
                if (position.ObjectPosition == null)
                {
                    position = Position.AfterLast;
                }
            }
            if (currentSegment == BufferEnd.Segment)
            {
                item = currentSegment.Buffer.Slice(currentSegment.Start, BufferEnd.Index - currentSegment.Start);
            }
            else
            {
                item = currentSegment.Buffer.Slice(currentSegment.Start, currentSegment.End - currentSegment.Start);
            }
            return true;
        }

        public ReadCursor Move(ReadCursor cursor, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            return cursor.Seek(count, BufferEnd, false);
        }
    }
}
