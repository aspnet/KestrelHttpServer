// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers
{
    public unsafe struct BufferHandle
    {
        IKnown _owner;
        GCHandle _handle;
        void* _pointer;

        public static BufferHandle Create<T>(OwnedBuffer<T> owner, int index, GCHandle handle = default(GCHandle))
        {
            void* pointer;
            if (owner.TryGetPointerInternal(out pointer))
            {
                pointer = Buffer<T>.Add(pointer, index);
            }
            else
            {
                ArraySegment<T> buffer;
                if (owner.TryGetArrayInternal(out buffer))
                {
                    handle = GCHandle.Alloc(buffer.Array, GCHandleType.Pinned);
                    pointer = Buffer<T>.Add((void*)handle.AddrOfPinnedObject(), buffer.Offset + index);
                }
                else
                {
                    throw new InvalidOperationException("Memory cannot be pinned");
                }
            }

            owner.AddReference();

            return new BufferHandle {
                _owner = owner,
                _handle = handle,
                _pointer = pointer
            };
        }

        public void* PinnedPointer
        {
            get
            {
                return _pointer;
            }
        }

        public void Free()
        {
            if (_owner != null)
            {
                if (_handle.IsAllocated)
                {
                    _handle.Free();
                }

                _owner.Release();
                _owner = null;
            }
        }
    }
}
