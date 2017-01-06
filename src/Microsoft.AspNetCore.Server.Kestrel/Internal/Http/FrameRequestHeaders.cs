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
    public partial class FrameRequestHeaders : FrameHeaders, IHeaderDictionary
    {
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

        protected override void SetValue(string key, ref StringValues value, bool overwrite)
            => SetValueFast(key, ref value, overwrite);

        protected override void Clear()
        {
            ClearFast();
        }

        public void Reset()
        {
            _isReadOnly = false;
            ClearFast();
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

        public unsafe void Append(byte[] keyBytes, int keyOffset, int keyLength, string value)
        {
            string key = null;
            fixed (byte* ptr = &keyBytes[keyOffset])
            {
                var pUL = (ulong*)ptr;

                if (keyLength < _keyByteDataByLength.Length)
                {
                    var candidates = _keyByteDataByLength[keyLength];

                    foreach (var candidate in candidates)
                    {
                        if (IsMatch(candidate, pUL))
                        {
                            var flag = 1L << candidate.Index;
                            if ((_bits & flag) != 0)
                            {
                                _headerData[candidate.Index] = StringValues.Concat(_headerData[candidate.Index], value);
                            }
                            else
                            {
                                _bits |= flag;
                                _headerData[candidate.Index] = new StringValues(value);
                            }
                            return;
                        }
                    }

                    key = ParseKey(keyLength, ptr);
                }
            }

            StringValues existing;
            Unknown.TryGetValue(key, out existing);
            Unknown[key] = StringValues.Concat(existing, value);
        }

        public unsafe void Append(byte* ptr, int keyLength, string value)
        {
            string key = null;
            var pUL = (ulong*)ptr;

            if (keyLength < _keyByteDataByLength.Length)
            {
                var candidates = _keyByteDataByLength[keyLength];

                foreach (var candidate in candidates)
                {
                    if (IsMatch(candidate, pUL))
                    {
                        var flag = 1L << candidate.Index;
                        if ((_bits & flag) != 0)
                        {
                            _headerData[candidate.Index] = StringValues.Concat(_headerData[candidate.Index], value);
                        }
                        else
                        {
                            _bits |= flag;
                            _headerData[candidate.Index] = new StringValues(value);
                        }
                        return;
                    }
                }

                key = ParseKey(keyLength, ptr);
            }

            StringValues existing;
            Unknown.TryGetValue(key, out existing);
            Unknown[key] = StringValues.Concat(existing, value);
        }

        private static unsafe bool IsMatch(HeaderKeyByteData candidate, ulong* pUL)
        {
            var isMatch = true;
            var compULongs = candidate.CompULongs;
            var maskULongs = candidate.MaskULongs;
            var arrayLength = maskULongs.Length;
            for (var i = 0; i < arrayLength; i++)
            {
                if ((pUL[i] & maskULongs[i]) != compULongs[i])
                {
                    isMatch = false;
                    break;
                }
            }

            ushort* pUS;
            var pUI = (uint*) (pUL + arrayLength);
            if (isMatch && candidate.CompUInt != 0)
            {
                pUS = (ushort*) (pUI + 1);
                isMatch = (*pUI & candidate.MaskUInt) == candidate.CompUInt;
            }
            else
            {
                pUS = (ushort*) pUI;
            }

            byte* pUB;
            if (isMatch && candidate.CompUShort != 0)
            {
                pUB = (byte*) (pUS + 1);
                isMatch = (*pUS & candidate.MaskUShort) == candidate.CompUShort;
            }
            else
            {
                pUB = (byte*) pUS;
            }

            if (isMatch && candidate.CompByte != 0 && (*pUB & candidate.MaskByte) != candidate.CompByte)
            {
                isMatch = false;
            }

            return isMatch;
        }

        private static unsafe string ParseKey(int keyLength, byte* ptr)
        {
            bool isValid;
            var key = new string('\0', keyLength);
            fixed (char* keyBuffer = key)
            {
                isValid = AsciiUtilities.TryGetAsciiString(ptr, keyBuffer, keyLength);
            }
            if (!isValid)
            {
                throw BadHttpRequestException.GetException(RequestRejectionReason.InvalidCharactersInHeaderName);
            }
            return key;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        protected override IEnumerator<KeyValuePair<string, StringValues>> GetEnumeratorFast()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<KeyValuePair<string, StringValues>>
        {
            private readonly FrameRequestHeaders _collection;
            private readonly long _bits;
            private int _state;
            private KeyValuePair<string, StringValues> _current;
            private readonly bool _hasUnknown;
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

        private class HeaderKeyByteData
        {
            public readonly int Index;
            public readonly ulong[] MaskULongs;
            public readonly ulong[] CompULongs;
            public readonly uint MaskUInt;
            public readonly uint CompUInt;
            public readonly ushort MaskUShort;
            public readonly ushort CompUShort;
            public readonly ushort MaskByte;
            public readonly ushort CompByte;

            public HeaderKeyByteData(int index, ulong[] maskULongs, ulong[] compULongs, uint maskUInt, uint compUInt, ushort maskUShort, ushort compUShort, byte maskByte, byte compByte)
            {
                Index = index;
                MaskULongs = maskULongs;
                CompULongs = compULongs;
                MaskUInt = maskUInt;
                CompUInt = compUInt;
                MaskUShort = maskUShort;
                CompUShort = compUShort;
                MaskByte = maskByte;
                CompByte = compByte;
            }
        }
    }
}
