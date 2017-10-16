// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.Runtime
{
    /// <summary>
    /// Make sure the struct is not copied, i.e. pass it only by reference
    /// </summary>
    /// <remarks>
    /// The  counter is not completly race-free. Reading GetGlobalCount and AddReference/Release are subject to a race.
    /// </remarks>
    public struct ReferenceCounter
    {
        // thread local counts that can be updated very efficiently
        [ThreadStatic]
        static ObjectTable t_threadLocalCounts;

        // all thread local counts; these are tallied up when global count is comupted
        static ObjectTable[] s_allTables = new ObjectTable[Environment.ProcessorCount];
        static int s_allTablesCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void AddReference(object obj)
        {
            var localCounts = t_threadLocalCounts;
            if (localCounts == null)
            {
                localCounts = AddThreadLocalTable();
            }
            localCounts.Increment(obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public void Release(object obj)
        {
            var localCounts = t_threadLocalCounts;
            localCounts.Decrement(obj);
        }

        // TODO: can we detect if the object was only refcounted on one thread and its the current thread? If yes, we don't need to synchronize?
        static public bool HasReference(object obj)
        {
            var allTables = s_allTables;
            lock (allTables)
            {
                for (int index = 0; index < s_allTablesCount; index++)
                {
                    if (allTables[index].HasReference(obj)) return true;
                }
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static public bool HasThreadLocalReference(object obj)
        {
            var localCounts = t_threadLocalCounts;
            if (localCounts == null) return false;
            return localCounts.HasReference(obj);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ObjectTable AddThreadLocalTable()
        {
            Debug.Assert(t_threadLocalCounts == null);
            var localCounts = new ObjectTable();
            t_threadLocalCounts = localCounts;
            lock (s_allTables)
            {
                    var allTables = s_allTables;
                    if (s_allTablesCount >= allTables.Length)
                    {
                        var newAllTables = new ObjectTable[allTables.Length << 1];
                        allTables.CopyTo(newAllTables, 0);
                        s_allTables = newAllTables;
                        allTables = newAllTables;
                    }
                    allTables[s_allTablesCount++] = localCounts;
            }
            return localCounts;
        }
    }

    // This datastructure holds a collection of objects to reference count mappings
    // Uses repetition of entries to represent the count.
    sealed class ObjectTable
    {
        // if you change this constant, update ResizingObjectTableWorks test.
        const int DefaultTableCapacity = 16;

        object[] _items;
        int _first;
        
        public ObjectTable()
        {
            _items = new object[DefaultTableCapacity];
            _first = _items.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Increment(object obj)
        {
            if (_first == 0)
            {
                // No space left, we must grow the table.
                var oldLength = _items.Length;
                var larger = new object[oldLength << 1];
                _items.CopyTo(larger, oldLength);
                // The copy must occur before the table is published.
                Volatile.Write(ref _items, larger);
                // _items must be updated before the value of _first is changed
                // If this is not the case, then we could effectively make the
                // table empty temporarily.
                _items[oldLength - 1] = obj;
                Volatile.Write(ref _first, oldLength - 1);
                return;
            }

            _items[_first - 1] = obj;
            // _items entry must update before we update the value of _first
            // If this is not the case, then a stale reference could
            // be observed.
            Volatile.Write(ref _first, _first - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Decrement(object obj)
        {
            for (int index = _first; index < _items.Length; index++)
            {
                if (ReferenceEquals(_items[index], obj))
                {
                    if (index != _first) {
                        //Condense the table
                        _items[index] = _items[_first];
                    }
                    // The condense must be visible before we update _first.
                    // If this is not the case, then we could lose a reference
                    // temporarily.
                    Volatile.Write(ref _first, _first + 1);
                    return;
                }
            }

            ThrowCountNotPositive();
        }

        private void ThrowCountNotPositive()
        {
            throw new InvalidOperationException("the object's count is not greater than zero");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasReference(object obj)
        {
            for (int index = _first; index < _items.Length; index++)
            {
                if (ReferenceEquals(_items[index], obj))
                {
                    return true;
                }
            }
            return false;
        }
    }
}