// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public class BufferMapper
    {
        private readonly object _mappingSync = new object();
        private BufferMapping[] _bufferIdMappings = new BufferMapping[0];

        public BufferMapper(MemoryPool memoryPool)
        {
            memoryPool.RegisterSlabAllocationCallback((slab) => OnSlabAllocated(slab));
            memoryPool.RegisterSlabDeallocationCallback((slab) => OnSlabDeallocated(slab));
        }

        internal RioRegisteredBuffer GetRegisteredBuffer(IntPtr address, out long startAddress)
        {
            var buffer = default(RioRegisteredBuffer);
            startAddress = 0;

            // Take local copy to avoid modifications
            var bufferIdMappings = _bufferIdMappings;
            var addressLong = address.ToInt64();

            // Can binary search if it's too slow
            for (var i = 0; i < bufferIdMappings.Length; i++)
            {
                var mapping = bufferIdMappings[i];
                if (addressLong >= mapping.Start && addressLong <= mapping.End)
                {
                    buffer = mapping.Buffer;
                    startAddress = mapping.Start;
                    break;
                }
            }

            return buffer;
        }

        internal unsafe RioBufferSegment GetSegmentFromBuffer(Buffer<byte> buffer)
        {
            // It's ok to unpin the handle here because the memory is from the pool
            // we created, which is already pinned.
            var pin = buffer.Pin();
            var spanPtr = (IntPtr)pin.PinnedPointer;
            pin.Free();

            long startAddress;
            long spanAddress = spanPtr.ToInt64();
            var bufferId = GetRegisteredBuffer(spanPtr, out startAddress);

            checked
            {
                var offset = unchecked((uint)(spanAddress - startAddress));
                return new RioBufferSegment(bufferId, offset, (uint)buffer.Length);
            }
        }

        private void OnSlabAllocated(MemoryPoolSlab slab)
        {
            var memoryPtr = slab.NativePointer;
            var buffer = RioRegisteredBuffer.Create(memoryPtr, (uint)slab.Length);
            var addressLong = memoryPtr.ToInt64();

            // Read, write and swap the mappings under lock
            lock (_mappingSync)
            {
                var currentMappings = _bufferIdMappings;
                var newMappings = new BufferMapping[currentMappings.Length + 1];

                for (var i = 0; i < currentMappings.Length; i++)
                {
                    newMappings[i] = currentMappings[i];
                }

                newMappings[currentMappings.Length] = new BufferMapping
                {
                    Buffer = buffer,
                    Start = addressLong,
                    End = addressLong + slab.Length
                };

                _bufferIdMappings = newMappings;
            }
        }

        private void OnSlabDeallocated(MemoryPoolSlab slab)
        {
            var memoryPtr = slab.NativePointer;
            var addressLong = memoryPtr.ToInt64();

            // Read, write and swap the mappings under lock
            lock (_mappingSync)
            {
                var currentMappings = _bufferIdMappings;
                var newMappings = new BufferMapping[currentMappings.Length - 1];

                for (int i = 0, n = 0; i < currentMappings.Length; i++)
                {
                    var bufferMapping = currentMappings[i];
                    if (addressLong != bufferMapping.Start)
                    {
                        // Not being removed, add it to new mappings
                        newMappings[n] = currentMappings[i];
                        n++;
                    }
                    else
                    {
                        // Being removed, dispose, don't add
                        bufferMapping.Buffer.Dispose();
                    }
                }

                _bufferIdMappings = newMappings;
            }
        }

        private struct BufferMapping
        {
            public RioRegisteredBuffer Buffer;
            public long Start;
            public long End;

            public override string ToString()
            {
                return $"{Buffer} ({Start}) - ({End})";
            }
        }
    }
}
