// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class MemoryPool : IMemoryPool
    {
        private static readonly byte[] EmptyArray = new byte[0];

        private readonly Pool<byte> _pool1 = new Pool<byte>();
        private readonly Pool<byte> _pool2 = new Pool<byte>();
        private readonly Pool<char> _pool3 = new Pool<char>();

        public byte[] Empty
        {
            get
            {
                return EmptyArray;
            }
        }

        public byte[] AllocByte(int minimumSize)
        {
            if (minimumSize == 0)
            {
                return EmptyArray;
            }
            if (minimumSize <= 1024)
            {
                return _pool1.Alloc(1024);
            }
            if (minimumSize <= 2048)
            {
                return _pool2.Alloc(2048);
            }
            return new byte[minimumSize];
        }

        public void FreeByte(byte[] memory)
        {
            if (memory == null)
            {
                return;
            }
            switch (memory.Length)
            {
                case 1024:
                    _pool1.Free(memory, 256);
                    break;
                case 2048:
                    _pool2.Free(memory, 64);
                    break;
            }
        }

        public char[] AllocChar(int minimumSize)
        {
            if (minimumSize == 0)
            {
                return new char[0];
            }
            if (minimumSize <= 128)
            {
                return _pool3.Alloc(128);
            }
            return new char[minimumSize];
        }

        public void FreeChar(char[] memory)
        {
            if (memory == null)
            {
                return;
            }
            switch (memory.Length)
            {
                case 128:
                    _pool3.Free(memory, 256);
                    break;
            }
        }

        public ArraySegment<byte> AllocSegment(int minimumSize)
        {
            return new ArraySegment<byte>(AllocByte(minimumSize));
        }

        public void FreeSegment(ArraySegment<byte> segment)
        {
            FreeByte(segment.Array);
        }

        class Pool<T>
        {
            private readonly ConcurrentQueue<T[]> _queue = new ConcurrentQueue<T[]>();

            public T[] Alloc(int size)
            {
                T[] item;
                if (_queue.TryDequeue(out item))
                {
                    return item;
                }
                return new T[size];
            }

            public void Free(T[] value, int limit)
            {
                if (_queue.Count < limit)
                {
                    _queue.Enqueue(value);
                }
            }
        }
    }
}
