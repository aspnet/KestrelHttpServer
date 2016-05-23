// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    public abstract class FrameHeaders : IHeaderDictionary
    {
        static readonly Vector<ushort> _minValidHeaderChar = new Vector<ushort>(0x20);
        static readonly int _vectorUShortSpan = Vector<ushort>.Count;

        protected bool _isReadOnly;
        protected Dictionary<string, StringValues> MaybeUnknown;

        protected Dictionary<string, StringValues> Unknown => MaybeUnknown ?? (MaybeUnknown = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase));

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
                    ThrowReadOnlyException();
                }
                SetValueFast(key, value);
            }
        }

        StringValues IDictionary<string, StringValues>.this[string key]
        {
            get
            {
                // Unlike the IHeaderDictionary version, this getter will throw a KeyNotFoundException.
                return GetValueFast(key);
            }
            set
            {
                ((IHeaderDictionary)this)[key] = value;
            }
        }

        protected void ThrowReadOnlyException()
        {
            throw new InvalidOperationException("Headers are read-only, response has already started.");
        }

        protected void ThrowArgumentException()
        {
            throw new ArgumentException();
        }

        protected void ThrowKeyNotFoundException()
        {
            throw new KeyNotFoundException();
        }

        protected void ThrowDuplicateKeyException()
        {
            throw new ArgumentException("An item with the same key has already been added.");
        }

        int ICollection<KeyValuePair<string, StringValues>>.Count => GetCountFast();

        bool ICollection<KeyValuePair<string, StringValues>>.IsReadOnly => _isReadOnly;

        ICollection<string> IDictionary<string, StringValues>.Keys => ((IDictionary<string, StringValues>)this).Select(pair => pair.Key).ToList();

        ICollection<StringValues> IDictionary<string, StringValues>.Values => ((IDictionary<string, StringValues>)this).Select(pair => pair.Value).ToList();

        public void SetReadOnly()
        {
            _isReadOnly = true;
        }

        public void Reset()
        {
            _isReadOnly = false;
            ClearFast();
        }

        protected static StringValues AppendValue(StringValues existing, string append)
        {
            return StringValues.Concat(existing, append);
        }

        protected static int BitCount(long value)
        {
            // see https://github.com/dotnet/corefx/blob/5965fd3756bc9dd9c89a27621eb10c6931126de2/src/System.Reflection.Metadata/src/System/Reflection/Internal/Utilities/BitArithmetic.cs

            const ulong Mask01010101 = 0x5555555555555555UL;
            const ulong Mask00110011 = 0x3333333333333333UL;
            const ulong Mask00001111 = 0x0F0F0F0F0F0F0F0FUL;
            const ulong Mask00000001 = 0x0101010101010101UL;

            var v = (ulong)value;

            v = v - ((v >> 1) & Mask01010101);
            v = (v & Mask00110011) + ((v >> 2) & Mask00110011);
            return (int)(unchecked(((v + (v >> 4)) & Mask00001111) * Mask00000001) >> 56);
        }

        protected virtual int GetCountFast()
        { throw new NotImplementedException(); }

        protected virtual StringValues GetValueFast(string key)
        { throw new NotImplementedException(); }

        protected virtual bool TryGetValueFast(string key, out StringValues value)
        { throw new NotImplementedException(); }

        protected virtual void SetValueFast(string key, StringValues value)
        { throw new NotImplementedException(); }

        protected virtual void AddValueFast(string key, StringValues value)
        { throw new NotImplementedException(); }

        protected virtual bool RemoveFast(string key)
        { throw new NotImplementedException(); }

        protected virtual void ClearFast()
        { throw new NotImplementedException(); }

        protected virtual void CopyToFast(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        { throw new NotImplementedException(); }

        protected virtual IEnumerator<KeyValuePair<string, StringValues>> GetEnumeratorFast()
        { throw new NotImplementedException(); }

        void ICollection<KeyValuePair<string, StringValues>>.Add(KeyValuePair<string, StringValues> item)
        {
            ((IDictionary<string, StringValues>)this).Add(item.Key, item.Value);
        }

        void IDictionary<string, StringValues>.Add(string key, StringValues value)
        {
            if (_isReadOnly)
            {
                ThrowReadOnlyException();
            }
            AddValueFast(key, value);
        }

        void ICollection<KeyValuePair<string, StringValues>>.Clear()
        {
            if (_isReadOnly)
            {
                ThrowReadOnlyException();
            }
            ClearFast();
        }

        bool ICollection<KeyValuePair<string, StringValues>>.Contains(KeyValuePair<string, StringValues> item)
        {
            StringValues value;
            return
                TryGetValueFast(item.Key, out value) &&
                value.Equals(item.Value);
        }

        bool IDictionary<string, StringValues>.ContainsKey(string key)
        {
            StringValues value;
            return TryGetValueFast(key, out value);
        }

        void ICollection<KeyValuePair<string, StringValues>>.CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            CopyToFast(array, arrayIndex);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumeratorFast();
        }

        IEnumerator<KeyValuePair<string, StringValues>> IEnumerable<KeyValuePair<string, StringValues>>.GetEnumerator()
        {
            return GetEnumeratorFast();
        }

        bool ICollection<KeyValuePair<string, StringValues>>.Remove(KeyValuePair<string, StringValues> item)
        {
            StringValues value;
            return
                TryGetValueFast(item.Key, out value) &&
                value.Equals(item.Value) &&
                RemoveFast(item.Key);
        }

        bool IDictionary<string, StringValues>.Remove(string key)
        {
            if (_isReadOnly)
            {
                ThrowReadOnlyException();
            }
            return RemoveFast(key);
        }

        bool IDictionary<string, StringValues>.TryGetValue(string key, out StringValues value)
        {
            return TryGetValueFast(key, out value);
        }

        public static void ValidateHeaderCharacters(StringValues headerValues)
        {
            foreach (var value in headerValues)
            {
                ValidateHeaderCharacters(value);
            }
        }

        public static void ValidateHeaderCharacters(string headerCharacters)
        {
            if (headerCharacters != null)
            {
                if (Vector.IsHardwareAccelerated)
                {
                    var remaining = headerCharacters.Length;
                    if (remaining < _vectorUShortSpan)
                    {
                        foreach (var ch in headerCharacters)
                        {
                            if (ch < 0x20)
                            {
                                throw new InvalidOperationException(string.Format("Invalid control character in header: 0x{0:X2}", (byte)ch));
                            }
                        }
                    }
                    else
                    {
                        VectorValidateHeaderCharacters(headerCharacters, remaining);
                    }                   
                }
                else
                {
                    foreach (var ch in headerCharacters)
                    {
                        if (ch < 0x20)
                        {
                            throw new InvalidOperationException(string.Format("Invalid control character in header: 0x{0:X2}", (byte)ch));
                        }
                    }                    
                }
            }
        }

        private static unsafe void VectorValidateHeaderCharacters(string headerCharacters, int remaining)
        {
            fixed (char* header = headerCharacters)
            {
                var offset = 0;
                while (remaining - _vectorUShortSpan >= 0)
                {
                    remaining -= _vectorUShortSpan;
                    var stringVector = Unsafe.Read<Vector<ushort>>(header + offset);
                    if (Vector.LessThanAny(stringVector, _minValidHeaderChar))
                    {
                        ThrowSpecficInvalidCharForVector(header + offset);
                    }

                    offset += _vectorUShortSpan;
                }
                
                while (remaining > 0)
                {
                    remaining--;
                    if (*(header + offset) < 0x20)
                    {
                        throw new InvalidOperationException(string.Format("Invalid control character in header: 0x{0:X2}", (byte)*(header + offset)));
                    }

                    offset++;
                }
            }
        }

        private unsafe static void ThrowSpecficInvalidCharForVector(char* header)
        {
            for (var i = 0; i < _vectorUShortSpan; i++)
            {
                if (*(header + i) < 0x20)
                {
                    throw new InvalidOperationException(string.Format("Invalid control character in header: 0x{0:X2}", (byte)*(header + i)));
                }
            }
        }
    }
}
