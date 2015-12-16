using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    /// <summary>
    /// Used to allocate and distribute re-usable blocks of memory.
    /// </summary>
    public class MemoryPool2
    {
        /// <summary>
        /// The gap between blocks' starting address. 4096 is chosen because most operating systems are 4k pages in size and alignment.
        /// </summary>
        private const int _blockStride = 4096;

        /// <summary>
        /// The last 64 bytes of a block are unused to prevent CPU from pre-fetching the next 64 byte into it's memory cache. 
        /// See https://github.com/aspnet/KestrelHttpServer/issues/117 and https://www.youtube.com/watch?v=L7zSU9HI-6I
        /// </summary>
        private const int _blockUnused = 64;

        /// <summary>
        /// Allocating 32 contiguous blocks per slab makes the slab size 128k. This is larger than the 85k size which will place the memory
        /// in the large object heap. This means the GC will not try to relocate this array, so the fact it remains pinned does not negatively
        /// affect memory management's compactification.
        /// </summary>
        internal const int _blockCount = 32;

        /// <summary>
        /// 4096 - 64 gives you a blockLength of 4032 usable bytes per block.
        /// </summary>
        private const int _blockLength = _blockStride - _blockUnused;
        
        /// <summary>
        /// Max allocation block size for pooled blocks, 
        /// larger values can be leased but they will be disposed after use rather than returned to the pool.
        /// </summary>
        public const int MaxPooledBlockLength = _blockLength;

        /// <summary>
        /// 4096 * 32 gives you a slabLength of 128k contiguous bytes allocated per slab
        /// </summary>
        private const int _slabLength = _blockStride * _blockCount;

        [ThreadStatic]
        private static InnerPool _pool;

        private InnerPool Pool
        {
            get
            {
                if (_pool == null)
                {
                    _pool = new InnerPool(this);
                }
                return _pool;
            }
        }

        /// <summary>
        /// Called to take a block from the pool.
        /// </summary>
        /// <param name="minimumLength">The block returned must be at least this size. It may be larger than this minimum size, and if so,
        /// the caller may write to the block's entire size rather than being limited to the minumumSize requested.</param>
        /// <returns>The block that is reserved for the called. It must be passed to Return when it is no longer being used.</returns>
        public virtual MemoryPoolBlock2 Lease(int minimumLength = MaxPooledBlockLength)
        {
            if (minimumLength > _blockLength)
            {
                return MemoryPoolBlock2.Create(
                    new ArraySegment<byte>(new byte[minimumLength]),
                    dataPtr: IntPtr.Zero,
                    pool: null,
                    slabId: -1);
            }

            return Pool.Lease();
        }

        /// <summary>
        /// Called to return a block to the pool. Once Return has been called the memory no longer belongs to the caller, and
        /// Very Bad Things will happen if the memory is read of modified subsequently. If a caller fails to call Return and the
        /// block tracking object is garbage collected, the block tracking object's finalizer will automatically re-create and return
        /// a new tracking object into the pool. This will only happen if there is a bug in the server, however it is necessary to avoid
        /// leaving "dead zones" in the slab due to lost block tracking objects.
        /// </summary>
        /// <param name="block">The block to return. It must have been acquired by calling Lease on the same memory pool instance.</param>
        public virtual void Return(MemoryPoolBlock2 block)
        {
            block.Pool?.Return(block);
        }

        private class InnerPool : MemoryPool2
        {
            private readonly static TimerCallback _livenessCallback = (o) => { CheckAlive((InnerPool)o); };
            private readonly int _owningThreadId;
            private readonly Timer _livenessCheck;
            private readonly MemoryPool2 _parentPool;
            private readonly object _returnLock;

            private WeakReference<Thread> _owningThread;

            private List<MemoryPoolSlab2> _slabs;
            private Queue<MemoryPoolBlock2> _availableBlocks;
            private Queue<MemoryPoolBlock2> _returnedBlocks;

            public InnerPool(MemoryPool2 parentPool)
            {
                _owningThread = new WeakReference<Thread>(Thread.CurrentThread);
                _owningThreadId = Thread.CurrentThread.ManagedThreadId;
                _parentPool = parentPool;
                _returnLock = new object();
                _availableBlocks = new Queue<MemoryPoolBlock2>(_blockCount * 2);
                _returnedBlocks = new Queue<MemoryPoolBlock2>(_blockCount * 2);
                _slabs = new List<MemoryPoolSlab2>(16);
                _livenessCheck = new Timer(_livenessCallback, this, 2000, 1000);
            }

            private void Dispose()
            {
                if (_availableBlocks == null) throw new ObjectDisposedException(nameof(InnerPool));
                _availableBlocks = null;
                _returnedBlocks = null;
                for (var i = 0; i < _slabs.Count; i++)
                {
                    _slabs[i].Dispose();
                    _slabs[i] = null;
                }
                _slabs = null;
            }

            static void CheckAlive(InnerPool pool)
            {
                pool.CheckAlive();
            }

            void CheckAlive()
            {
                Thread owningThread;
                if (!_owningThread.TryGetTarget(out owningThread))
                {
                    _owningThread = null;
                    _livenessCheck.Change(Timeout.Infinite, Timeout.Infinite);
                    _livenessCheck.Dispose();
                    Dispose();
                }
            }

            public override MemoryPoolBlock2 Lease(int minimumLength)
            {
                // Called directly, return to parent pool for correct thread's pool
                return _parentPool.Lease(minimumLength);
            }

            public MemoryPoolBlock2 Lease()
            {
                if (_owningThread == null) throw new ObjectDisposedException(nameof(InnerPool));

                // Lease is always same thread
                if (_availableBlocks.Count > 0)
                {
                    var block = _availableBlocks.Dequeue();
                    _slabs[block.SlabId].Leased();
                    return block;
                }

                // retun queue can conflict with Return on multiple threads
                lock (_returnLock)
                {
                    // Empty return queue
                    while (_returnedBlocks.Count > 0)
                    {
                        var block = _returnedBlocks.Dequeue();
                        _availableBlocks.Enqueue(block);
                    }
                }

                if (_availableBlocks.Count > 0)
                {
                    var block = _availableBlocks.Dequeue();
                    _slabs[block.SlabId].Leased();
                    return block;
                }

                return Allocate();
            }

            public override void Return(MemoryPoolBlock2 block)
            {
                block.Reset();
                if (_owningThread == null) return;

                if (Thread.CurrentThread.ManagedThreadId == _owningThreadId)
                {
                    // Owning thread, add to available queue
                    _availableBlocks.Enqueue(block);
                    _slabs[block.SlabId].Returned();
                }
                else
                {
                    // Return from non-owning thread, add to return queue
                    lock (_returnLock)
                    {
                        _returnedBlocks.Enqueue(block);
                        _slabs[block.SlabId].Returned();
                    }
                }
            }

            private MemoryPoolBlock2 Allocate()
            {
                var slab = MemoryPoolSlab2.Create(_slabLength);
                var slabId = _slabs.Count;
                _slabs.Add(slab);

                var basePtr = slab.ArrayPtr;
                var firstOffset = (int)((_blockStride - 1) - ((ulong)(basePtr + _blockStride - 1) % _blockStride));

                var poolAllocationLength = _slabLength - _blockStride;

                var offset = firstOffset;
                for (;
                    offset + _blockLength < poolAllocationLength;
                    offset += _blockStride)
                {
                    var block = MemoryPoolBlock2.Create(
                        new ArraySegment<byte>(slab.Array, offset, _blockLength),
                        basePtr,
                        this,
                        slabId);

                    _availableBlocks.Enqueue(block);
                }

                // return last block rather than adding to pool
                var newBlock = MemoryPoolBlock2.Create(
                        new ArraySegment<byte>(slab.Array, offset, _blockLength),
                        basePtr,
                        this,
                        slabId);

                return newBlock;
            }
        }
    }
}
