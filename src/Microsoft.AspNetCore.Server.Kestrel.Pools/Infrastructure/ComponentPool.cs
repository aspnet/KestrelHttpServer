// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Pools.Infrastructure
{
    public class ComponentPool<T> : ComponentPool where T : class
    {
        private readonly T[] _objects;

        private CacheLinePadded<int> _index;
        private McsStackLock _mcsLock;

        /// <summary>
        /// Creates the pool with maxPooled objects.
        /// </summary>
        public ComponentPool(int maxPooled)
        {
            if (maxPooled <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPooled));
            }

            _index = new CacheLinePadded<int>() { Value = -1 };
            _objects = new T[maxPooled];
        }

        /// <summary>Tries to take an object from the pool, returns true if sucessful.</summary>
        public unsafe bool TryRent(out T obj)
        {
            T[] objects = _objects;
            obj = null;

            try
            {
                // Protect lock+unlock from Thread.Abort
            }
            finally
            {
                var ticket = new LockTicket();
                _mcsLock.Lock(&ticket);

                var removeIndex = _index.Value;
                if (removeIndex >= 0)
                {
                    obj = objects[removeIndex];
                    objects[removeIndex] = null;
                    _index.Value = removeIndex - 1;
                }

                _mcsLock.Unlock(&ticket);
            }

            return obj != null;
        }

        /// <summary>
        /// Attempts to return the object to the pool.  If successful, the object will be stored
        /// in the pool; otherwise, the object won't be stored.
        /// </summary>
        public unsafe void Return(T obj)
        {
            if (obj == null)
            {
                return;
            }

            try
            {
                // Protect lock+unlock from Thread.Abort
            }
            finally
            {
                var ticket = new LockTicket();
                _mcsLock.Lock(&ticket);

                var insertIndex = _index.Value + 1;
                if (insertIndex < _objects.Length)
                {
                    _objects[insertIndex] = obj;
                    _index.Value = insertIndex;
                }

                _mcsLock.Unlock(&ticket);
            }
        }
    }

    public class ComponentPool
    {
        /// <summary>
        /// McsStackLock is a specialized MCS Lock.
        /// It is a fair scalable starvation free First In First Out (FIFO) queued spinlock.
        /// It does not allocate for its queues, however lock and unlock must only be used in same stack scope.
        /// It has no false sharing or cache invalidation on lock tickets so it scales with processor count.
        /// Both lock and unlock must be used within a finally block to prevent Thread.Abort Exceptions cuasing live locks
        /// </summary>
        protected unsafe struct McsStackLock
        {
            private CacheLinePadded<LockTicket> _queue;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Lock(LockTicket* ticket)
            {
                (*ticket).IsBlocked = true;

                var previous = Interlocked.Exchange(ref _queue.Value.Next, (IntPtr)ticket);
                if (previous == IntPtr.Zero)
                {
                    // Not blocked
                    return;
                }

                Interlocked.Exchange(ref (*(LockTicket*)previous).Next, (IntPtr)ticket);

                var isBlocked = Volatile.Read(ref (*ticket).IsBlocked);
                while (isBlocked)
                {
                    isBlocked = Volatile.Read(ref (*ticket).IsBlocked);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Unlock(LockTicket* ticket)
            {
                if ((*ticket).Next == IntPtr.Zero)
                {
                    if (Interlocked.CompareExchange(ref _queue.Value.Next, IntPtr.Zero, (IntPtr)ticket) == (IntPtr)ticket)
                    {
                        // Unlocked. Nothing waiting
                        return;
                    };

                    // Item waiting - however not yet attached
                    var next = Volatile.Read(ref (*ticket).Next);
                    while (next == IntPtr.Zero)
                    {
                        next = Volatile.Read(ref (*ticket).Next);
                    }

                    Volatile.Write(ref (*((LockTicket*)next)).IsBlocked, false);
                    // Unlocked - Next waiting notified
                }
                else
                {
                    Volatile.Write(ref (*(LockTicket*)(*ticket).Next).IsBlocked, false);
                    // Unlocked - Next waiting notified
                }
            }
        }

        protected struct LockTicket
        {
            public bool IsBlocked;
            public IntPtr Next;
        }
    }
}
