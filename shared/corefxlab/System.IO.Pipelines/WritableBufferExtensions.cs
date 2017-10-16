// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    /// <summary>
    /// Common extension methods against writable buffers
    /// </summary>
    public static class WritableBufferExtensions
    {
        /// <summary>
        /// Writes the source <see cref="Span{Byte}"/> to the <see cref="WritableBuffer"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="WritableBuffer"/></param>
        /// <param name="source">The <see cref="Span{Byte}"/> to write</param>
        public static void Write(this WritableBuffer buffer, ReadOnlySpan<byte> source)
        {
            if (buffer.Buffer.IsEmpty)
            {
                buffer.Ensure();
            }

            // Fast path, try copying to the available memory directly
            if (source.Length <= buffer.Buffer.Length)
            {
                source.CopyTo(buffer.Buffer.Span);
                buffer.Advance(source.Length);
                return;
            }

            var remaining = source.Length;
            var offset = 0;

            while (remaining > 0)
            {
                var writable = Math.Min(remaining, buffer.Buffer.Length);

                buffer.Ensure(writable);

                if (writable == 0)
                {
                    continue;
                }

                source.Slice(offset, writable).CopyTo(buffer.Buffer.Span);

                remaining -= writable;
                offset += writable;

                buffer.Advance(writable);
            }
        }

        public static void Write(this WritableBuffer buffer, byte[] source)
        {
            Write(buffer, source, 0, source.Length);
        }

        public static void Write(this WritableBuffer buffer, byte[] source, int offset, int length)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            if (length == 0)
            {
                return;
            }

            Span<byte> dest = default(Span<byte>);
            var destLength = dest.Length;
            if (destLength == 0)
            {
                buffer.Ensure();

                // Get the new span and length
                dest = buffer.Buffer.Span;
                destLength = dest.Length;
            }

            var sourceLength = length;
            if (sourceLength <= destLength)
            {
                ref byte pSource = ref source[offset];
                ref byte pDest = ref dest.DangerousGetPinnableReference();
                Unsafe.CopyBlockUnaligned(ref pDest, ref pSource, (uint)sourceLength);
                buffer.Advance(sourceLength);
                return;
            }

            WriteMultiBuffer(buffer, source, offset, length);
        }

        private static void WriteMultiBuffer(WritableBuffer buffer, byte[] source, int offset, int length)
        {
            var remaining = length;

            while (remaining > 0)
            {
                var writable = Math.Min(remaining, buffer.Buffer.Length);

                buffer.Ensure(writable);

                if (writable == 0)
                {
                    continue;
                }

                ref byte pSource = ref source[offset];
                ref byte pDest = ref buffer.Buffer.Span.DangerousGetPinnableReference();

                Unsafe.CopyBlockUnaligned(ref pDest, ref pSource, (uint)writable);

                remaining -= writable;
                offset += writable;

                buffer.Advance(writable);
            }
        }
    }
}
