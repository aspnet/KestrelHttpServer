// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Runtime;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers.Internal
{
    internal class OwnerEmptyMemory<T> : OwnedBuffer<T>
    {
        readonly static T[] s_empty = new T[0];
        public readonly static OwnedBuffer<T> Shared = new OwnerEmptyMemory<T>();

        public override int Length => s_empty.Length;

        public override Span<T> Span
        {
            get
            {
                if (IsDisposed) BufferPrimitivesThrowHelper.ThrowObjectDisposedException(nameof(OwnerEmptyMemory<T>));
                return s_empty;
            }
        }

        protected override void Dispose(bool disposing)
        {}

        protected internal override bool TryGetArrayInternal(out ArraySegment<T> buffer)
        {
            if (IsDisposed) BufferPrimitivesThrowHelper.ThrowObjectDisposedException(nameof(OwnerEmptyMemory<T>));
            buffer = new ArraySegment<T>(s_empty);
            return true;
        }

        protected internal override unsafe bool TryGetPointerInternal(out void* pointer)
        {
            pointer = null;
            return false;
        }
    }
}