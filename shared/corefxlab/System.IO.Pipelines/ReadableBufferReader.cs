// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    public struct ReadableBufferReader
    {
        private Span<byte> _currentSpan;
        private int _index;
        private SegmentEnumerator _enumerator;
        private int _consumedBytes;
        private bool _end;

        public ReadableBufferReader(ReadableBuffer buffer) : this(buffer.Start, buffer.End)
        {
        }

        public ReadableBufferReader(ReadCursor start, ReadCursor end) : this()
        {
            _end = false;
            _index = 0;
            _consumedBytes = 0;
            _enumerator = new SegmentEnumerator(start, end);
            _currentSpan = default(Span<byte>);
            MoveNext();
        }

        public bool End => _end;

        public int Index => _index;

        public ReadCursor Cursor
        {
            get
            {
                var part = _enumerator.Current;

                if (_end)
                {
                    return new ReadCursor(part.Segment, part.Start + _currentSpan.Length);
                }

                return new ReadCursor(part.Segment, part.Start + _index);
            }
        }

        public Span<byte> Span => _currentSpan;

        public int ConsumedBytes => _consumedBytes;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Peek()
        {
            if (_end)
            {
                return -1;
            }
            return _currentSpan[_index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Take()
        {
            if (_end)
            {
                return -1;
            }

            var value = _currentSpan[_index];

            _index++;
            _consumedBytes++;

            if (_index >= _currentSpan.Length)
            {
                MoveNext();
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void MoveNext()
        {
            while (_enumerator.MoveNext())
            {
                var part = _enumerator.Current;
                var length = part.Length;
                if (length != 0)
                {
                    _currentSpan = part.Segment.Buffer.Span.Slice(part.Start, length);
                    _index = 0;
                    return;
                }
            }

            _end = true;
        }

        public void Skip(int length)
        {
            if (length < 0)
            {
                PipelinesThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.length);
            }

            _consumedBytes += length;

            while (!_end && length > 0)
            {
                if ((_index + length) < _currentSpan.Length)
                {
                    _index += length;
                    length = 0;
                    break;
                }

                length -= (_currentSpan.Length - _index);
                MoveNext();
            }

            if (length > 0)
            {
                PipelinesThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.length);
            }
        }
    }
}