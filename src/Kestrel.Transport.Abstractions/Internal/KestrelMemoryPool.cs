// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal
{
    public static class KestrelMemoryPool
    {
        public static MemoryPool<byte> Create(bool supressDisposeException = false)
        {
            MemoryPool<byte> pool = new SlabMemoryPool();
            // Pool should not be throwing in release
#if DEBUG
            if (supressDisposeException)
            {
                pool = new NonThrowingMemoryPool(pool);
            }
#endif
            return pool;
        }

        public static readonly int MinimumSegmentSize = 4096;

        private class NonThrowingMemoryPool: MemoryPool<byte>
        {
            private readonly MemoryPool<byte> _pool;

            public NonThrowingMemoryPool(MemoryPool<byte> pool)
            {
                _pool = pool;
            }

            protected override void Dispose(bool disposing)
            {
                try
                {
                    _pool.Dispose();
                }
                catch (Exception)
                {
                    // ignore
                }
            }

            public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
            {
                return _pool.Rent(minBufferSize);
            }

            public override int MaxBufferSize => _pool.MaxBufferSize;
        }
    }
}
