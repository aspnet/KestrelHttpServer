using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    /// <summary>
    /// Slab tracking object used by the byte buffer memory pool. A slab is a large allocation which is divided into smaller blocks. The
    /// individual blocks are then treated as independant array segments.
    /// </summary>
    internal class MemoryPoolSlab2 : IDisposable
    {
        private bool _isDisposed = false; // To detect redundant calls
        private long _leasedBlocks = 1; // One checked out on creation
        private long _returnedBlocks = 0; 

        private Timer _livenessCheck;
        private static TimerCallback _livenessCallback = (o) => { CheckAlive((MemoryPoolSlab2)o); };

        /// <summary>
        /// This handle pins the managed array in memory until the slab is disposed. This prevents it from being
        /// relocated and enables any subsections of the array to be used as native memory pointers to P/Invoked API calls.
        /// </summary>
        private GCHandle _gcHandle;

        /// <summary>
        /// The managed memory allocated in the large object heap.
        /// </summary>
        public byte[] Array;

        /// <summary>
        /// The native memory pointer of the pinned Array. All block native addresses are pointers into the memory 
        /// ranging from ArrayPtr to ArrayPtr + Array.Length
        /// </summary>
        public IntPtr ArrayPtr;

        public static MemoryPoolSlab2 Create(int length)
        {
            // allocate and pin requested memory length
            var array = new byte[length];
            var gcHandle = GCHandle.Alloc(array, GCHandleType.Pinned);

            // allocate and return slab tracking object
            return new MemoryPoolSlab2
            {
                Array = array,
                _gcHandle = gcHandle,
                ArrayPtr = gcHandle.AddrOfPinnedObject()
            };
        }

        public void Leased()
        {
            // Only called on single thread
            _leasedBlocks++;
        }

        public void Returned()
        {
            // Can be called by multiple threads
            Interlocked.Increment(ref _returnedBlocks);
            if (_isDisposed)
            {
                FreeHandle();
            }
        }

        private bool FreeHandle()
        {
            var outstanding = Volatile.Read(ref _leasedBlocks) - Volatile.Read(ref _returnedBlocks);
            if (outstanding < 0)
            {
                throw new InvalidOperationException("Too many " + nameof(MemoryPoolBlock2) + " returned to " + nameof(MemoryPoolSlab2));
            }
            else if (outstanding > MemoryPool2._blockCount)
            {
                throw new InvalidOperationException("Too many " + nameof(MemoryPoolBlock2) + " taken from " + nameof(MemoryPoolSlab2));
            }
            else if (outstanding != 0)
            {
                return false;
            }
            try
            {
                _gcHandle.Free();
            }
            catch (InvalidOperationException)
            {
                // Free race
            }
            return true;
        }
        static void CheckAlive(MemoryPoolSlab2 slab)
        {
            slab.CheckAlive();
        }

        void CheckAlive()
        {
            if (FreeHandle())
            {
                _livenessCheck.Change(Timeout.Infinite, Timeout.Infinite);
                _livenessCheck.Dispose();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                Array = null;
                if (!FreeHandle())
                {
                    _livenessCheck = new Timer(_livenessCallback, this, 2000, 1000);
                }
            }
        }
        
        ~MemoryPoolSlab2()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }
        
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
