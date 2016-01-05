// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public partial class FrameResponseHeaders : FrameHeaders
    {
        private static readonly byte[] _CrLf = new[] { (byte)'\r', (byte)'\n' };
        private static readonly byte[] _colonSpace = new[] { (byte)':', (byte)' ' };

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        protected override IEnumerator<KeyValuePair<string, StringValues>> GetEnumeratorFast()
        {
            return GetEnumerator();
        }

        public void CopyTo(ref MemoryPoolIterator2 output)
        {
            CopyToFast(ref output);
            if (MaybeUnknown != null && MaybeUnknown.Count > 0)
            {
                foreach (var kv in MaybeUnknown)
                {
                    var ul = kv.Value.Count;
                    for (var i = 0; i < ul; i++)
                    {
                        var value = kv.Value[i];
                        if (value != null)
                        {
                            output.CopyFrom(_CrLf, 0, 2);
                            output.CopyFromAscii(kv.Key);
                            output.CopyFrom(_colonSpace, 0, 2);
                            output.CopyFromAscii(value);
                        }
                    }
                }
            }
        }

        public partial struct Enumerator : IEnumerator<KeyValuePair<string, StringValues>>
        {
            private readonly FrameResponseHeaders _collection;
            private readonly long _bits;
            private int _state;
            private KeyValuePair<string, StringValues> _current;
            private readonly bool _hasUnknown;
            private Dictionary<string, StringValues>.Enumerator _unknownEnumerator;

            internal Enumerator(FrameResponseHeaders collection)
            {
                _collection = collection;
                _bits = collection._bits;
                _state = 0;
                _current = default(KeyValuePair<string, StringValues>);
                _hasUnknown = collection.MaybeUnknown != null;
                _unknownEnumerator = _hasUnknown
                    ? collection.MaybeUnknown.GetEnumerator()
                    : default(Dictionary<string, StringValues>.Enumerator);
            }

            public KeyValuePair<string, StringValues> Current => _current;

            object IEnumerator.Current => _current;

            public void Dispose()
            {
            }

            public void Reset()
            {
                _state = 0;
            }
        }
    }
}
