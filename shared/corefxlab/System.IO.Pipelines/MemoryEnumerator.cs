// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    /// <summary>
    /// An enumerator over the <see cref="ReadableBuffer"/>
    /// </summary>
    public struct BufferEnumerator
    {
        private SegmentEnumerator _segmentEnumerator;
        private Buffer<byte> _current;

        /// <summary>
        /// 
        /// </summary>
        public BufferEnumerator(ReadCursor start, ReadCursor end)
        {
            _segmentEnumerator = new SegmentEnumerator(start, end);
            _current = default(Buffer<byte>);
        }

        /// <summary>
        /// The current <see cref="Buffer{Byte}"/>
        /// </summary>
        public Buffer<byte> Current => _current;

        /// <summary>
        /// Moves to the next <see cref="Buffer{Byte}"/> in the <see cref="ReadableBuffer"/>
        /// </summary>
        /// <returns></returns>
        public bool MoveNext()
        {
            if (!_segmentEnumerator.MoveNext())
            {
                _current = default(Buffer<byte>);
                return false;
            }
            var current = _segmentEnumerator.Current;
            _current = current.Segment.Buffer.Slice(current.Start, current.Length);

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Reset()
        {
            PipelinesThrowHelper.ThrowNotSupportedException();
        }
    }
}