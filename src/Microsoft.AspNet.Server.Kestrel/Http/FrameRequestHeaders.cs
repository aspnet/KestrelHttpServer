// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using Microsoft.AspNet.Http.Features.Enumerators;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public partial class FrameRequestHeaders : FrameHeaders, IEnumeratorIndexer<string, StringValues>
    {

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        protected override IEnumerator<KeyValuePair<string, StringValues>> GetEnumeratorFast()
        {
            return GetEnumerator();
        }

        public override StringValuesDictEnumerator GetInterfaceEnumerator()
        {
            if (MaybeUnknown != null && MaybeUnknown.Count > 0)
            {
                return new StringValuesDictEnumerator(new IndexingEnumerator<string, StringValues>(this, MaybeUnknown.GetEnumerator()));
            }
            else
            {
                return new StringValuesDictEnumerator(new IndexingEnumerator<string, StringValues>(this));
            }
        }

        IndexerMoveNextResult<KeyValuePair<string, StringValues>> IEnumeratorIndexer<string, StringValues>.MoveNext(int currentIndex, bool hasDictionaryState, ref Dictionary<string, StringValues>.Enumerator dictionaryState)
        {
            return MoveNextForEnumerator(currentIndex, hasDictionaryState, ref dictionaryState);
        }

        public partial struct Enumerator : IEnumerator<KeyValuePair<string, StringValues>>
        {
            private FrameRequestHeaders _collection;
            private long _bits;
            private int _state;
            private KeyValuePair<string, StringValues> _current;
            private bool _hasUnknown;
            private Dictionary<string, StringValues>.Enumerator _unknownEnumerator;

            internal Enumerator(FrameRequestHeaders collection)
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
