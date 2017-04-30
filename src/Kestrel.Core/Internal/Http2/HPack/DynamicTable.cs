// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack
{
    public class DynamicTable
    {
        private readonly LinkedList<HeaderField> _table = new LinkedList<HeaderField>();

        private int _maxSize = 4096;
        private int _size;

        public HeaderField this[int index]
        {
            get
            {
                var node = _table.First;

                for (var i = 0; i < index; i++)
                {
                    node = node.Next;
                }

                return node.Value;
            }
        }

        public void Insert(string name, string value)
        {
            var entrySize = name.Length + value.Length + 32;
            EnsureSize(_maxSize - entrySize);

            _table.AddFirst(new HeaderField(name, value));
            _size += entrySize;
        }

        public void Resize(int maxSize)
        {
            _maxSize = maxSize;
            EnsureSize(_maxSize);
        }

        public void EnsureSize(int size)
        {
            while (_size > size)
            {
                _size -= _table.Last.Value.Name.Length + _table.Last.Value.Value.Length + 32;
                _table.RemoveLast();
            }
        }
    }
}
