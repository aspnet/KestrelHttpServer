// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    public class StringCache : IStringCache
    {
        private int _maxCached; 
        private int _maxCachedStringLength;

        private readonly uint[] _hashes;
        private readonly int[] _lastUse;
        private readonly string[] _strings;

        private int _currentUse = 0;

        // x64 int array byte size (28 + length * 4) rounded up to 8 bytes
        // x86 int array byte size (12 + length * 4) rounded up to 4 bytes
        // Array of 25 ints is 2 consecutive cache lines on x64; second prefetched
        // Array of 9 ints is 1 cache line on x64
        public StringCache() : this(25, 256)
        {
        }

        public StringCache(int maxCached, int maxCachedStringLength)
        {
            _maxCached = maxCached;
            _maxCachedStringLength = maxCachedStringLength;
            _hashes = new uint[maxCached];
            _lastUse = new int[maxCached];
            _strings = new string[maxCached];
        }

        public void MarkStart()
        {
            _currentUse++;
        }

        public unsafe string GetString(char* data, uint hash, int length)
        {
            if (length > _maxCachedStringLength)
            {
                return new string(data, 0, length);
            }

            int oldestEntry = int.MaxValue;
            int oldestIndex = 0;

            for (var i = 0; i < _maxCached; i++)
            {
                var usage = _lastUse[i];
                if (oldestEntry > usage)
                {
                    oldestEntry = usage;
                    oldestIndex = i;
                }

                if (hash == _hashes[i])
                {
                    var cachedString = _strings[i];
                    if (cachedString.Length != length)
                    {
                        continue;
                    }

                    fixed(char* cs = cachedString)
                    {
                        var cached = cs;
                        var potential = data;

                        var c = 0;
                        var lengthMinusSpan = length - 3;
                        for (; c < lengthMinusSpan; c += 4)
                        {
                            if(
                                *(cached) != *(potential) ||
                                *(cached + 1) != *(potential + 1) ||
                                *(cached + 2) != *(potential + 2) ||
                                *(cached + 3) != *(potential + 3)
                            )
                            {
                                continue;
                            }
                            cached += 4;
                            potential += 4;
                        }
                        for (; c < length; c++)
                        {
                            if (*(cached++) != *(potential++))
                            {
                                continue;
                            }
                        }
                    }

                    _lastUse[i] = _currentUse;
                    // same string
                    return cachedString;
                }
            }

            var value = new string(data, 0, length);

            _lastUse[oldestIndex] = _currentUse;
            _hashes[oldestIndex] = hash;
            _strings[oldestIndex] = value;

            return value;
        }
    }
}
