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
    public struct WritableBufferWriter
    {
        private WritableBuffer _writableBuffer;
        private Span<byte> _span;

        public WritableBufferWriter(WritableBuffer writableBuffer)
        {
            _writableBuffer = writableBuffer;
            _span = writableBuffer.Buffer.Span;
        }

        public Span<byte> Span => _span;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int count)
        {
            _span = _span.Slice(count);
            _writableBuffer.Advance(count);
        }

        public void Write(byte[] source)
        {
            if (source.Length > 0 && _span.Length >= source.Length)
            {
                ref byte pSource = ref source[0];
                ref byte pDest = ref _span.DangerousGetPinnableReference();

                Unsafe.CopyBlockUnaligned(ref pDest, ref pSource, (uint)source.Length);

                Advance(source.Length);
            }
            else
            {
                WriteMultiBuffer(source, 0, source.Length);
            }
        }

        public void Write(byte[] source, int offset, int length)
        {
            // If offset or length is negative the cast to uint will make them larger than int.MaxValue
            // so each test both tests for negative values and greater than values. This pattern wil also
            // elide the second bounds check that would occur at source[offset]; as is pre-checked
            // https://github.com/dotnet/coreclr/pull/9773
            if ((uint)offset > (uint)source.Length || (uint)length > (uint)(source.Length - offset))
            {
                // Only need to pass in array length and offset for ThrowHelper to determine which test failed
                PipelinesThrowHelper.ThrowArgumentOutOfRangeException(source.Length, offset);
            }

            if (length > 0 && _span.Length >= length)
            {
                ref byte pSource = ref source[offset];
                ref byte pDest = ref _span.DangerousGetPinnableReference();

                Unsafe.CopyBlockUnaligned(ref pDest, ref pSource, (uint)length);

                Advance(length);
            }
            else
            {
                WriteMultiBuffer(source, offset, length);
            }
        }

        private void WriteMultiBuffer(byte[] source, int offset, int length)
        {
            var remaining = length;

            while (remaining > 0)
            {
                if (_span.Length == 0)
                {
                    Ensure();
                }

                var writable = Math.Min(remaining, _span.Length);

                ref byte pSource = ref source[offset];
                ref byte pDest = ref _span.DangerousGetPinnableReference();

                Unsafe.CopyBlockUnaligned(ref pDest, ref pSource, (uint)writable);

                Advance(writable);

                remaining -= writable;
                offset += writable;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Ensure(int count = 1)
        {
            _writableBuffer.Ensure(count);
            _span = _writableBuffer.Buffer.Span;
        }
    }
}