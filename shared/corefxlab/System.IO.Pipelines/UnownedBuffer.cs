// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    /// <summary>
    /// Represents a buffer that is owned by an external component.
    /// </summary>
    public class UnownedBuffer : OwnedBuffer<byte>
    {
        public UnownedBuffer(ArraySegment<byte> buffer)
        {
            _buffer = buffer;
        }

        public override int Length => _buffer.Count;

        public override Span<byte> Span
        {
            get
            {
                if (IsDisposed) PipelinesThrowHelper.ThrowObjectDisposedException(nameof(UnownedBuffer));
                return new Span<byte>(_buffer.Array, _buffer.Offset, _buffer.Count);
            }
        }

        public OwnedBuffer<byte> MakeCopy(int offset, int length, out int newStart, out int newEnd)
        {
            // Copy to a new Owned Buffer.
            var buffer = new byte[length];
            global::System.Buffer.BlockCopy(_buffer.Array, _buffer.Offset + offset, buffer, 0, length);
            newStart = 0;
            newEnd = length;
            return buffer;
        }

// In kestrel both MemoryPoolBlock and OwnedBuffer end up in the same assembly so
// this method access modifiers need to be `protected internal`
#if KESTREL_BY_SOURCE
        internal
#endif
        protected override bool TryGetArrayInternal(out ArraySegment<byte> buffer)
        {
            if (IsDisposed) PipelinesThrowHelper.ThrowObjectDisposedException(nameof(UnownedBuffer));
            buffer = _buffer;
            return true;
        }

// In kestrel both MemoryPoolBlock and OwnedBuffer end up in the same assembly so
// this method access modifiers need to be `protected internal`
#if KESTREL_BY_SOURCE
        internal
#endif
        protected override unsafe bool TryGetPointerInternal(out void* pointer)
        {
            pointer = null;
            return false;
        }

        private ArraySegment<byte> _buffer;
    }
}
