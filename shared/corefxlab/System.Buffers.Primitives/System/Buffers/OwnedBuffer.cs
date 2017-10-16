// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Runtime;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers
{
    public abstract class OwnedBuffer<T> : IDisposable, IKnown
    {
        protected OwnedBuffer() { }

        public abstract int Length { get; }

        public abstract Span<T> Span { get; }

        public Buffer<T> Buffer => new Buffer<T>(this, 0, Length);

        public ReadOnlyBuffer<T> ReadOnlyBuffer => new ReadOnlyBuffer<T>(this, 0, Length);

        public static implicit operator OwnedBuffer<T>(T[] array) => new Internal.OwnedArray<T>(array);

        #region Lifetime Management
        public bool IsDisposed => _disposed;

        public void Dispose()
        {
            if (HasOutstandingReferences) throw new InvalidOperationException("outstanding references detected.");
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            _disposed = disposing;
        }

        public bool HasOutstandingReferences
        {
            get
            {
                return Volatile.Read(ref _referenceCount) > 0
                        || (ReferenceCountingSettings.OwnedMemory == ReferenceCountingMethod.ReferenceCounter
                            && ReferenceCounter.HasReference(this));
            }
        }

        public void AddReference()
        {
            Interlocked.Increment(ref _referenceCount);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _referenceCount) == 0)
                OnZeroReferences();
        }

        protected virtual void OnZeroReferences()
        { }

        public virtual BufferHandle Pin(int index = 0)
        {
            return BufferHandle.Create(this, index);
        }
        #endregion

        internal protected abstract bool TryGetArrayInternal(out ArraySegment<T> buffer);

        internal protected abstract unsafe bool TryGetPointerInternal(out void* pointer);

        bool _disposed;
        int _referenceCount;
    }
}
