// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public partial class FrameResponseHeaders : FrameHeaders, IHeaderDictionary
    {
        private static readonly byte[] _CrLf = new[] { (byte)'\r', (byte)'\n' };
        private static readonly byte[] _colonSpace = new[] { (byte)':', (byte)' ' };

        private long? _contentLength;

        public bool HasConnection => HeaderConnection.Count != 0;

        public bool HasTransferEncoding => HeaderTransferEncoding.Count != 0;

        public bool HasContentLength => _contentLength.HasValue;

        public bool HasServer => HeaderServer.Count != 0;

        public bool HasDate => HeaderDate.Count != 0;

        public long? HeaderContentLengthValue => _contentLength;

        protected override void SetValue(string key, ref StringValues value, bool overwrite)
            => SetValueFast(key, ref value, overwrite);

        StringValues IHeaderDictionary.this[string key]
        {
            get
            {
                StringValues value;
                TryGetValueFast(key, out value);
                return value;
            }
            set
            {
                if (_isReadOnly)
                {
                    ThrowHeadersReadOnlyException();
                }

                SetValueFast(key, ref value, true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetValueFast(string key, ref StringValues value, bool overwrite)
        {
            ValidateHeaderCharacters(ref value);

            var length = key.Length;
            if (length < _keyStringDataByLength.Length)
            {
                foreach (var candidate in _keyStringDataByLength[length])
                {
                    if (IsMatch(key, candidate))
                    {
                        var index = candidate.Index;
                        var flag = 1L << index;
                        if ((_bits & flag) != 0)
                        {
                            if (!overwrite)
                            {
                                ThrowDuplicateKeyException();
                            }

                            ClearExtra(index);
                        }


                        if (index == (int)HeaderIndex.ContentLength)
                        {
                            _contentLength = ParseContentLength(ref value);
                        }

                        _bits |= flag;
                        _headerData[index] = value;
                        return;
                    }
                }
            }

            ValidateHeaderCharacters(key);

            if (!overwrite && Unknown.ContainsKey(key))
            {
                ThrowDuplicateKeyException();
            }

            Unknown[key] = value;
        }

        protected override void Clear()
        {
            _contentLength = null;
            ClearFast();
        }

        public void Reset()
        {
            _isReadOnly = false;
            _contentLength = null;
            ClearFast();
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        protected override IEnumerator<KeyValuePair<string, StringValues>> GetEnumeratorFast()
        {
            return GetEnumerator();
        }

        private void CopyExtraTo(ref MemoryPoolIterator output)
        {
            foreach (var kv in MaybeUnknown)
            {
                foreach (var value in kv.Value)
                {
                    if (value != null)
                    {
                        output.CopyFrom(_CrLf, 0, 2);
                        output.CopyFromAscii(kv.Key);
                        output.CopyFrom(_colonSpace, 0, 2);
                        output.CopyFromAscii(value);
                    }
                }
            }
            MaybeUnknown.Clear();
        }

        public struct Enumerator : IEnumerator<KeyValuePair<string, StringValues>>
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

            public bool MoveNext()
            {
                while (_state < HeaderNames.Length)
                {
                    var header = HeaderNames[_state];
                    _state++;
                    if (((_bits & (1L << header.Value)) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>(header.Key, _collection._headerData[header.Value]);
                        return true;
                    }
                }

                if (!_hasUnknown || !_unknownEnumerator.MoveNext())
                {
                    _current = default(KeyValuePair<string, StringValues>);
                    return false;
                }
                _current = _unknownEnumerator.Current;
                return true;
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
