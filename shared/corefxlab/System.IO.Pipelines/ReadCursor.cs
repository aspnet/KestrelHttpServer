// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    public struct ReadCursor : IEquatable<ReadCursor>
    {
        internal BufferSegment Segment;
        internal int Index;

        internal ReadCursor(BufferSegment segment)
        {
            Segment = segment;
            Index = segment?.Start ?? 0;
        }

        internal ReadCursor(BufferSegment segment, int index)
        {
            Segment = segment;
            Index = index;
        }

        internal bool IsDefault => Segment == null;

        internal bool IsEnd
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var segment = Segment;

                if (segment == null)
                {
                    return true;
                }
                else if (Index < segment.End)
                {
                    return false;
                }
                else if (segment.Next == null)
                {
                    return true;
                }
                else
                {
                    return IsEndMultiSegment();
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool IsEndMultiSegment()
        {
            var segment = Segment.Next;
            while (segment != null)
            {
                if (segment.Start < segment.End)
                {
                    return false; // subsequent block has data - IsEnd is false
                }
                segment = segment.Next;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetLength(ReadCursor end)
        {
            if (IsDefault)
            {
                return 0;
            }
            return GetLength(Segment, Index, end.Segment, end.Index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetLength(
            BufferSegment start,
            int startIndex,
            BufferSegment endSegment,
            int endIndex)
        {
            if (start == endSegment)
            {
                return endIndex - startIndex;
            }

            return GetLengthMultiSegment(start, startIndex, endSegment, endIndex);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int GetLengthMultiSegment(BufferSegment start,
            int startIndex,
            BufferSegment endSegment,
            int endIndex)
        {
            var segment = start;
            var index = startIndex;
            var length = 0;
            checked
            {
                while (true)
                {
                    if (segment == endSegment)
                    {
                        return length + endIndex - index;
                    }
                    else if (segment.Next == null)
                    {
                        return length;
                    }
                    else
                    {
                        length += segment.End - index;
                        segment = segment.Next;
                        index = segment.Start;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadCursor Seek(int bytes, ReadCursor end, bool checkEndReachable = true)
        {
            if (IsEnd)
            {
                return this;
            }

            ReadCursor cursor;
            if (Segment == end.Segment && end.Index - Index >= bytes)
            {
                cursor = new ReadCursor(Segment, Index + bytes);
            }
            else
            {
                cursor = SeekMultiSegment(bytes, end, checkEndReachable);
            }

            return cursor;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ReadCursor SeekMultiSegment(int bytes, ReadCursor end, bool checkEndReachable)
        {
            ReadCursor result = default(ReadCursor);
            bool foundResult = false;

            foreach (var segmentPart in new SegmentEnumerator(this, end))
            {
                // We need to loop up until the end to make sure start and end are connected
                // if end is not trusted
                if (!foundResult)
                {
                    if (segmentPart.Length >= bytes)
                    {
                        result = new ReadCursor(segmentPart.Segment, segmentPart.Start + bytes);
                        foundResult = true;
                        if (!checkEndReachable)
                        {
                            break;
                        }
                    }
                    else
                    {
                        bytes -= segmentPart.Length;
                    }
                }

            }
            if (!foundResult)
            {
                ThrowOutOfBoundsException();
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void BoundsCheck(ReadCursor newCursor)
        {
            if (!this.GreaterOrEqual(newCursor))
            {
                ThrowOutOfBoundsException();
            }
        }

        private static void ThrowOutOfBoundsException()
        {
            throw new InvalidOperationException("Cursor is out of bounds");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetBuffer(ReadCursor end, out Buffer<byte> data)
        {
            if (IsDefault)
            {
                data = Buffer<byte>.Empty;
                return false;
            }

            var segment = Segment;
            var index = Index;

            if (end.Segment == segment)
            {
                var following = end.Index - index;

                if (following > 0)
                {
                    data = segment.Buffer.Slice(index, following);
                    return true;
                }

                data = Buffer<byte>.Empty;
                return false;
            }
            else
            {
                return TryGetBufferMultiBlock(end, out data);
            }
        }

        private bool TryGetBufferMultiBlock(ReadCursor end, out Buffer<byte> data)
        {
            var segment = Segment;
            var index = Index;

            // Determine if we might attempt to copy data from segment.Next before
            // calculating "following" so we don't risk skipping data that could
            // be added after segment.End when we decide to copy from segment.Next.
            // segment.End will always be advanced before segment.Next is set.

            int following = 0;

            while (true)
            {
                var wasLastSegment = segment.Next == null || end.Segment == segment;

                if (end.Segment == segment)
                {
                    following = end.Index - index;
                }
                else
                {
                    following = segment.End - index;
                }

                if (following > 0)
                {
                    break;
                }

                if (wasLastSegment)
                {
                    data = Buffer<byte>.Empty;
                    return false;
                }
                else
                {
                    segment = segment.Next;
                    index = segment.Start;
                }
            }

            data = segment.Buffer.Slice(index, following);
            return true;
        }

        public override string ToString()
        {
            if (IsEnd)
            {
                return "<end>";
            }

            var sb = new StringBuilder();
            Span<byte> span = Segment.Buffer.Span.Slice(Index, Segment.End - Index);
            SpanExtensions.AppendAsLiteral(span, sb);
            return sb.ToString();
        }

        public static bool operator ==(ReadCursor c1, ReadCursor c2)
        {
            return c1.Equals(c2);
        }

        public static bool operator !=(ReadCursor c1, ReadCursor c2)
        {
            return !c1.Equals(c2);
        }

        public bool Equals(ReadCursor other)
        {
            return other.Segment == Segment && other.Index == Index;
        }

        public override bool Equals(object obj)
        {
            return Equals((ReadCursor)obj);
        }

        public override int GetHashCode()
        {
            var h1 = Segment?.GetHashCode() ?? 0;
            var h2 = Index.GetHashCode();

            var shift5 = ((uint)h1 << 5) | ((uint)h1 >> 27);
            return ((int)shift5 + h1) ^ h2;
        }

        internal bool GreaterOrEqual(ReadCursor other)
        {
            if (other.Segment == Segment)
            {
                return other.Index <= Index;
            }
            return IsReachable(other);
        }

        internal bool IsReachable(ReadCursor other)
        {
            var current = other.Segment;
            while (current != null)
            {
                if (current == Segment)
                {
                    return true;
                }
                current = current.Next;
            }
            return false;
        }
    }
}
