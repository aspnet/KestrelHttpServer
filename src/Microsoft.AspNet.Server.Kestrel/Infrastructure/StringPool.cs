// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    public class StringPool
    {
        private const int _maxCached = 18;
        private const int _maxCacheLength = 256;

        private readonly ulong[] _hashes = new ulong[_maxCached];
        private readonly int[] _lastUse = new int[_maxCached];
        private readonly string[] _strings = new string[_maxCached];

        private int _currentUse = 0;

        public void MarkStart()
        {
            _currentUse++;
        }

        public unsafe string GetString(ulong hash, char* data, int length)
        {
            if (length > _maxCacheLength)
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
