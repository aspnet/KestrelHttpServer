// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Pools.Infrastructure
{
    public abstract class ComponentFactory<T> : IComponentFactory<T> where T : class, IComponent
    {
        private static readonly int _poolCount = CalculatePoolCount();
        private static readonly int _poolMask = _poolCount - 1;

        private CacheLinePadded<int> _rentPoolIndex = new CacheLinePadded<int>();
        private CacheLinePadded<int> _returnPoolIndex = new CacheLinePadded<int>();
        private ComponentPool<T>[] _pools = new ComponentPool<T>[_poolCount];

        private int _maxPooled;
        private int _maxPerPool;

        public int MaxPooled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _maxPooled; }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxPooled));
                if (value != _maxPooled)
                {
                    var maxPerPool = (int)Math.Ceiling(value / (double)_poolCount);
                    if (maxPerPool == 0 && value > 0)
                    {
                        maxPerPool = 1;
                    }

                    Interlocked.Exchange(ref _pools, CreatePools(_poolCount, maxPerPool));

                    _maxPooled = value;
                    _maxPerPool = maxPerPool;
                }
            }
        }

        public ComponentFactory()
            : this(CalculatePoolCount() * 128)
        {
        }

        public ComponentFactory(int maxPooled)
        {
            MaxPooled = maxPooled;
        }

        private ComponentPool<T> RentPool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            { 
                return Volatile.Read(ref _pools[Interlocked.Increment(ref _rentPoolIndex.Value) & _poolMask]);
            }
        }

        private ComponentPool<T> ReturnPool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Volatile.Read(ref _pools[Interlocked.Increment(ref _returnPoolIndex.Value) & _poolMask]);
            }
        }

        protected abstract T CreateNew();

        public void Dispose(ref T component, bool requestImmediateReuse)
        {
            if (MaxPooled > 0)
            {
                if (requestImmediateReuse)
                {
                    component.Reset();
                }
                else
                {
                    component.Uninitialize();
                    ReturnPool.Return(component);
                    component = null;
                }
            }
            else
            {
                component = null;
            }
        }

        public T Create()
        {
            if (_maxPooled == 0)
            {
                return CreateNew();
            }

            T component = null;
            if (!RentPool.TryRent(out component))
            {
                component = CreateNew();
            }
            return component;
        }

        private static ComponentPool<T>[] CreatePools(int poolCount, int maxPerPool)
        {
            var pools = new ComponentPool<T>[poolCount];
            for (var i = 0; i < pools.Length; i++)
            {
                pools[i] = new ComponentPool<T>(maxPerPool);
            }

            return pools;
        }

        private static int CalculatePoolCount()
        {
            var processors = Environment.ProcessorCount;

            if (processors > 64) return 256;
            if (processors > 32) return 128;
            if (processors > 16) return 64;
            if (processors > 8) return 32;
            if (processors > 4) return 16;
            if (processors > 2) return 8;
            if (processors > 1) return 4;
            return 2;
        }
    }
}
