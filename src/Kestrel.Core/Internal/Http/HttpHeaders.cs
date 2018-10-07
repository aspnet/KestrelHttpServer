// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public abstract class HttpHeaders : IHeaderDictionary
    {
        protected long? _contentLength;
        protected bool _isReadOnly;
        protected Dictionary<string, StringValues> MaybeUnknown;
        protected Dictionary<string, StringValues> Unknown => MaybeUnknown ?? (MaybeUnknown = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase));

        public long? ContentLength
        {
            get { return _contentLength; }
            set
            {
                if (value.HasValue && value.Value < 0)
                {
                    ThrowInvalidContentLengthException(value.Value);
                }
                _contentLength = value;
            }
        }

        StringValues IHeaderDictionary.this[string key]
        {
            get
            {
                TryGetValueFast(key, out var value);
                return value;
            }
            set
            {
                if (_isReadOnly)
                {
                    ThrowHeadersReadOnlyException();
                }
                if (string.IsNullOrEmpty(key))
                {
                    ThrowInvalidEmptyHeaderName();
                }
                if (value.Count == 0)
                {
                    RemoveFast(key);
                }
                else
                {
                    SetValueFast(key, value);
                }
            }
        }

        StringValues IDictionary<string, StringValues>.this[string key]
        {
            get
            {
                // Unlike the IHeaderDictionary version, this getter will throw a KeyNotFoundException.
                if (!TryGetValueFast(key, out var value))
                {
                    ThrowKeyNotFoundException();
                }
                return value;
            }
            set
            {
                ((IHeaderDictionary)this)[key] = value;
            }
        }

        protected void ThrowHeadersReadOnlyException()
        {
            throw new InvalidOperationException(CoreStrings.HeadersAreReadOnly);
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
            throw new ArgumentException(CoreStrings.KeyAlreadyExists);
        }

        public int Count => GetCountFast();

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

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static StringValues AppendValue(in StringValues existing, string append)
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

        protected virtual bool TryGetValueFast(string key, out StringValues value)
        { throw new NotImplementedException(); }

        protected virtual void SetValueFast(string key, in StringValues value)
        { throw new NotImplementedException(); }

        protected virtual bool AddValueFast(string key, in StringValues value)
        { throw new NotImplementedException(); }

        protected virtual bool RemoveFast(string key)
        { throw new NotImplementedException(); }

        protected virtual void ClearFast()
        { throw new NotImplementedException(); }

        protected virtual bool CopyToFast(KeyValuePair<string, StringValues>[] array, int arrayIndex)
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
                ThrowHeadersReadOnlyException();
            }
            if (string.IsNullOrEmpty(key))
            {
                ThrowInvalidEmptyHeaderName();
            }

            if (value.Count > 0 && !AddValueFast(key, value))
            {
                ThrowDuplicateKeyException();
            }
        }

        void ICollection<KeyValuePair<string, StringValues>>.Clear()
        {
            if (_isReadOnly)
            {
                ThrowHeadersReadOnlyException();
            }
            ClearFast();
        }

        bool ICollection<KeyValuePair<string, StringValues>>.Contains(KeyValuePair<string, StringValues> item)
        {
            return
                TryGetValueFast(item.Key, out var value) &&
                value.Equals(item.Value);
        }

        bool IDictionary<string, StringValues>.ContainsKey(string key)
        {
            StringValues value;
            return TryGetValueFast(key, out value);
        }

        void ICollection<KeyValuePair<string, StringValues>>.CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            if (!CopyToFast(array, arrayIndex))
            {
                ThrowArgumentException();
            }
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
            return
                TryGetValueFast(item.Key, out var value) &&
                value.Equals(item.Value) &&
                RemoveFast(item.Key);
        }

        bool IDictionary<string, StringValues>.Remove(string key)
        {
            if (_isReadOnly)
            {
                ThrowHeadersReadOnlyException();
            }
            return RemoveFast(key);
        }

        bool IDictionary<string, StringValues>.TryGetValue(string key, out StringValues value)
        {
            return TryGetValueFast(key, out value);
        }

        public static void ValidateHeaderValueCharacters(in StringValues headerValues)
        {
            var count = headerValues.Count;
            for (var i = 0; i < count; i++)

            {
                ValidateHeaderValueCharacters(headerValues[i]);
            }
        }

        public static void ValidateHeaderValueCharacters(string headerCharacters)
        {
            if (headerCharacters != null)
            {
                var invalid = HttpCharacters.IndexOfInvalidFieldValueChar(headerCharacters);
                if (invalid >= 0)
                {
                    ThrowInvalidHeaderCharacter(headerCharacters[invalid]);
                }
            }
        }

        public static void ValidateHeaderNameCharacters(string headerCharacters)
        {
            var invalid = HttpCharacters.IndexOfInvalidTokenChar(headerCharacters);
            if (invalid >= 0)
            {
                ThrowInvalidHeaderCharacter(headerCharacters[invalid]);
            }
        }

        public static ConnectionOptions ParseConnection(in StringValues connection)
        {
            var connectionOptions = ConnectionOptions.None;

            var connectionCount = connection.Count;
            for (var i = 0; i < connectionCount; i++)
            {
                var value = connection[i].AsSpan();
                var currentPosition = 0;
                while (currentPosition < value.Length)
                {
                    var token = GetToken(value, currentPosition);
                    currentPosition += token.Length;
                    var offset = 0;
                    if (token.Length >= 9 && (token[offset] | 0x20) == 'k')
                    {
                        if ((token[++offset] | 0x20) == 'e' &&
                            (token[++offset] | 0x20) == 'e' &&
                            (token[++offset] | 0x20) == 'p' &&
                            token[++offset] == '-' &&
                            (token[++offset] | 0x20) == 'a' &&
                            (token[++offset] | 0x20) == 'l' &&
                            (token[++offset] | 0x20) == 'i' &&
                            (token[++offset] | 0x20) == 'v' &&
                            (token[++offset] | 0x20) == 'e' &&
                            IsTokenEndValid(token, ++offset))
                        {
                            connectionOptions |= ConnectionOptions.KeepAlive;
                        }
                    }
                    else if (token.Length >= 7 && (token[offset] | 0x20) == 'u')
                    {
                        if ((token[++offset] | 0x20) == 'p' &&
                            (token[++offset] | 0x20) == 'g' &&
                            (token[++offset] | 0x20) == 'r' &&
                            (token[++offset] | 0x20) == 'a' &&
                            (token[++offset] | 0x20) == 'd' &&
                            (token[++offset] | 0x20) == 'e' &&
                            IsTokenEndValid(token, ++offset))
                        {
                            connectionOptions |= ConnectionOptions.Upgrade;
                        }
                    }
                    else if (token.Length >= 5 && (token[offset] | 0x20) == 'c')
                    {
                        if ((token[++offset] | 0x20) == 'l' &&
                            (token[++offset] | 0x20) == 'o' &&
                            (token[++offset] | 0x20) == 's' &&
                            (token[++offset] | 0x20) == 'e' &&
                            IsTokenEndValid(token, ++offset))
                        {
                            connectionOptions |= ConnectionOptions.Close;
                        }
                    }
                }
            }

            return connectionOptions;
        }

        public static TransferCoding GetFinalTransferCoding(in StringValues transferEncoding)
        {
            var transferEncodingOptions = TransferCoding.None;
            for (var i = 0; i < transferEncoding.Count; i++)
            {
                var value = transferEncoding[i].AsSpan();
                var currentPosition = 0;
                while (currentPosition < value.Length)
                {
                    var token = GetToken(value, currentPosition);
                    currentPosition += token.Length;
                    var offset = 0;
                    if (token.Length >= 7 && (token[offset] | 0x20) == 'c')
                    {
                        if ((token[++offset] | 0x20) == 'h' &&
                            (token[++offset] | 0x20) == 'u' &&
                            (token[++offset] | 0x20) == 'n' &&
                            (token[++offset] | 0x20) == 'k' &&
                            (token[++offset] | 0x20) == 'e' &&
                            (token[++offset] | 0x20) == 'd' &&
                            IsTokenEndValid(token, ++offset))
                        {
                            transferEncodingOptions = TransferCoding.Chunked;
                        }
                    }

                    if (token.Length > 0 && offset != token.Length)
                    {
                        transferEncodingOptions = TransferCoding.Other;
                    }
                }
            }
            return transferEncodingOptions;
        }

        private static bool IsTokenEndValid(in ReadOnlySpan<char> token, int offset)
        {
            while (offset < token.Length && token[offset] == ' ')
            {
                offset++;
            }

            return offset == token.Length;
        }

        private static ReadOnlySpan<char> GetToken(in ReadOnlySpan<char> value, int startPos)
        {
            var tokenLength = 0;
            while (tokenLength < value.Length && value[tokenLength] != ',')
            {
                tokenLength++;
            }

            var tokenStart = startPos;
            while (tokenStart < tokenLength && value[tokenStart] == ' ')
            {
                tokenStart++;
            }

            return value.Slice(tokenStart, tokenLength);
        }

        private static void ThrowInvalidContentLengthException(long value)
        {
            throw new ArgumentOutOfRangeException(CoreStrings.FormatInvalidContentLength_InvalidNumber(value));
        }

        private static void ThrowInvalidHeaderCharacter(char ch)
        {
            throw new InvalidOperationException(CoreStrings.FormatInvalidAsciiOrControlChar(string.Format("0x{0:X4}", (ushort)ch)));
        }

        private static void ThrowInvalidEmptyHeaderName()
        {
            throw new InvalidOperationException(CoreStrings.InvalidEmptyHeaderName);
        }
    }
}
