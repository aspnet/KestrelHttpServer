// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    internal struct SegmentEnumerator
    {
        private BufferSegment _segment;
        private SegmentPart _current;
        private int _startIndex;
        private readonly int _endIndex;
        private readonly BufferSegment _endSegment;

        /// <summary>
        /// 
        /// </summary>
        public SegmentEnumerator(ReadCursor start, ReadCursor end)
        {
            _startIndex = start.Index;
            _segment = start.Segment;
            _endSegment = end.Segment;
            _endIndex = end.Index;
            _current = default(SegmentPart);
        }

        /// <summary>
        /// The current <see cref="Buffer{Byte}"/>
        /// </summary>
        public SegmentPart Current => _current;

        /// <summary>
        /// Moves to the next <see cref="Buffer{Byte}"/> in the <see cref="ReadableBuffer"/>
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            var segment = _segment;

            if (segment == null)
            {
                return false;
            }

            var start = _startIndex;
            var end = segment.End;

            if (segment == _endSegment)
            {
                end = _endIndex;
                _segment = null;
            }
            else
            {
                _segment = segment.Next;
                if (_segment == null)
                {
                    if (_endSegment != null)
                    {
                        ThrowEndNotSeen();
                    }
                }
                else
                {
                    _startIndex = _segment.Start;
                }
            }

            _current = new SegmentPart()
            {
                Segment = segment,
                Start = start,
                End = end,
            };

            return true;
        }

        private void ThrowEndNotSeen()
        {
            throw new InvalidOperationException("Segments ended by end was never seen");
        }

        public SegmentEnumerator GetEnumerator()
        {
            return this;
        }

        public void Reset()
        {
            PipelinesThrowHelper.ThrowNotSupportedException();
        }

        internal struct SegmentPart
        {
            public BufferSegment Segment;
            public int Start;
            public int End;

            public int Length => End - Start;
        }
    }
}