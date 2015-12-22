﻿using System;
using System.Text;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    /// <summary>
    /// Block tracking object used by the byte buffer memory pool. A slab is a large allocation which is divided into smaller blocks. The
    /// individual blocks are then treated as independant array segments.
    /// </summary>
    public class MemoryPoolBlock2
    {
        /// <summary>
        /// The array segment describing the range of memory this block is tracking. The caller which has leased this block may only read and
        /// modify the memory in this range.
        /// </summary>
        public ArraySegment<byte> Data;

        /// <summary>
        /// This object cannot be instantiated outside of the static Create method
        /// </summary>
        protected MemoryPoolBlock2()
        {
        }

        /// <summary>
        /// Back-reference to the memory pool which this block was allocated from. It may only be returned to this pool.
        /// </summary>
        public MemoryPool2 Pool { get; private set; }

        /// <summary>
        /// Back-reference to the slab from which this block was taken, or null if it is one-time-use memory.
        /// </summary>
        public MemoryPoolSlab2 Slab { get; private set; }

        /// <summary>        
        /// /// Native address of the first byte of this block's Data memory. It is null for one-time-use memory, or copied from 
        /// the Slab's ArrayPtr for a slab-block segment. The byte it points to corresponds to Data.Array[0], and in practice you will always
        /// use the _dataArrayPtr + Start or _dataArrayPtr + End, which point to the start of "active" bytes, or point to just after the "active" bytes.
        /// 
        /// Called to ensure that a block is pinned, and return the pointer to the native address
        /// of the first byte of this block's Data memory. Arriving data is read into Pin() + End.
        /// Outgoing data is read from Pin() + Start.
        /// </summary>
        /// <returns></returns>
        public IntPtr Pin { get; private set; }

        public unsafe byte* Pointer { get; private set; }

        /// <summary>
        /// Convenience accessor
        /// </summary>
        public byte[] Array => Data.Array;

        /// <summary>
        /// The Start represents the offset into Array where the range of "active" bytes begins. At the point when the block is leased
        /// the Start is guaranteed to be equal to Array.Offset. The value of Start may be assigned anywhere between Data.Offset and
        /// Data.Offset + Data.Count, and must be equal to or less than End.
        /// </summary>
        public int Start { get; set; }

        /// <summary>
        /// The End represents the offset into Array where the range of "active" bytes ends. At the point when the block is leased
        /// the End is guaranteed to be equal to Array.Offset. The value of Start may be assigned anywhere between Data.Offset and
        /// Data.Offset + Data.Count, and must be equal to or less than End.
        /// </summary>
        public int End { get; set; }

        /// <summary>
        /// Reference to the next block of data when the overall "active" bytes spans multiple blocks. At the point when the block is
        /// leased Next is guaranteed to be null. Start, End, and Next are used together in order to create a linked-list of discontiguous 
        /// working memory. The "active" memory is grown when bytes are copied in, End is increased, and Next is assigned. The "active" 
        /// memory is shrunk when bytes are consumed, Start is increased, and blocks are returned to the pool.
        /// </summary>
        public MemoryPoolBlock2 Next { get; set; }

        public unsafe static MemoryPoolBlock2 Create(
            ArraySegment<byte> data,
            IntPtr dataPtr,
            MemoryPool2 pool,
            MemoryPoolSlab2 slab)
        {
            return new MemoryPoolBlock2
            {
                Data = data,
                Pin = dataPtr,
                Pool = pool,
                Slab = slab,
                Start = data.Offset,
                End = data.Offset,
                Pointer = (byte*)(dataPtr.ToPointer())
            };
        }

        /// <summary>
        /// called when the block is returned to the pool. mutable values are re-assigned to their guaranteed initialized state.
        /// </summary>
        public void Reset()
        {
            Next = null;
            Start = Data.Offset;
            End = Data.Offset;
        }

        /// <summary>
        /// ToString overridden for debugger convenience. This displays the "active" byte information in this block as ASCII characters.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return Encoding.ASCII.GetString(Array, Start, End - Start);
        }

        /// <summary>
        /// acquires a cursor pointing into this block at the Start of "active" byte information
        /// </summary>
        /// <returns></returns>
        public MemoryPoolIterator2 GetIterator()
        {
            return new MemoryPoolIterator2(this);
        }
    }
}
