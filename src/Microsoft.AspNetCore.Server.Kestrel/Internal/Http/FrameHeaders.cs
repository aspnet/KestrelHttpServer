// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public abstract class FrameHeaders : IDictionary<string, StringValues>
    {
        protected static readonly HeaderKeyStringData[] NoHeaders = new HeaderKeyStringData[0];
#if NETSTANDARD1_3
        protected static readonly ulong[] ShortHeader = Array.Empty<ulong>();
#else
        protected static readonly ulong[] ShortHeader = new ulong[0];
#endif
        private readonly HeaderKeyStringData[][] _keyStringDataByLength;

        protected readonly StringValues[] _headerData;
        protected readonly KeyValuePair<string, int>[] _headerKeyNames;
        protected long _bits = 0;
        protected bool _isReadOnly;

        protected Dictionary<string, StringValues> MaybeUnknown;

        protected Dictionary<string, StringValues> Unknown => MaybeUnknown ?? (MaybeUnknown = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase));

        protected FrameHeaders(KeyValuePair<string, int>[] headerKeyNames, HeaderKeyStringData[][] keyStringDataByLength, StringValues[] headerData)
        {
            _keyStringDataByLength = keyStringDataByLength;
            _headerData = headerData;
            _headerKeyNames = headerKeyNames;
        }

        StringValues IDictionary<string, StringValues>.this[string key]
        {
            get
            {
                // Unlike the IHeaderDictionary version, this getter will throw a KeyNotFoundException.
                StringValues value;
                if (!TryGetValueFast(key, out value))
                {
                    ThrowKeyNotFoundException();
                }

                return value;
            }
            set
            {
                if (_isReadOnly)
                {
                    ThrowHeadersReadOnlyException();
                }

                SetValue(key, ref value, true);
            }
        }

        protected abstract void SetValue(string key, ref StringValues value, bool overwrite);

        int ICollection<KeyValuePair<string, StringValues>>.Count => BitCount(_bits) + (MaybeUnknown?.Count ?? 0);

        bool ICollection<KeyValuePair<string, StringValues>>.IsReadOnly => _isReadOnly;

        ICollection<string> IDictionary<string, StringValues>.Keys => this.Select(pair => pair.Key).ToList();

        ICollection<StringValues> IDictionary<string, StringValues>.Values => this.Select(pair => pair.Value).ToList();

        public void SetReadOnly()
        {
            _isReadOnly = true;
        }

        protected bool TryGetValueFast(string key, out StringValues value)
        {
            var length = key.Length;
            if (length < _keyStringDataByLength.Length)
            {
                foreach (var candidate in _keyStringDataByLength[length])
                {
                    if (IsMatch(key, candidate))
                    {
                        if (((_bits & (1L << candidate.Index)) != 0))
                        {
                            value = _headerData[candidate.Index];
                            return true;
                        }

                        return false;
                    }
                }
            }

            return MaybeUnknown?.TryGetValue(key, out value) ?? false;
        }

        private bool RemoveFast(string key)
        {
            var length = key.Length;
            if (length < _keyStringDataByLength.Length)
            {
                foreach (var candidate in _keyStringDataByLength[length])
                {
                    if (IsMatch(key, candidate))
                    {
                        var flag = (1L << candidate.Index);
                        if (((_bits & flag) != 0))
                        {
                            _bits &= ~flag;
                            _headerData[candidate.Index] = StringValues.Empty;
                            ClearExtra(candidate.Index);
                            return true;
                        }

                        return false;
                    }
                }
            }

            return MaybeUnknown?.Remove(key) ?? false;
        }

        protected static unsafe bool IsMatch(string key, HeaderKeyStringData candidate)
        {
            var isMatch = true;
            fixed (char* ptr = key)
            {
                var pUL = (ulong*) ptr;

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

                if (isMatch && candidate.CompUShort != 0 && ((*pUS & candidate.MaskUShort) != candidate.CompUShort))
                {
                    isMatch = false;
                }
            }

            return isMatch;
        }

        protected abstract void Clear();

        protected virtual void ClearExtra(int index) { }

        protected void ClearFast()
        {
            MaybeUnknown?.Clear();
            var bits = _bits;
            if (bits == 0)
            {
                return;
            }

            _bits = 0;

            var flag = 1L;
            var headers = _headerData;
            for (var i = 0; i < headers.Length; i++)
            {
                var hasHeader = (bits & flag) != 0;
                flag = 1L << (i + 1);

                if (hasHeader)
                {
                    headers[i] = default(StringValues);

                    if (bits < flag)
                    {
                        break;
                    }
                }
            }
        }

        void ICollection<KeyValuePair<string, StringValues>>.Add(KeyValuePair<string, StringValues> item)
            => ((IDictionary<string, StringValues>)this).Add(item.Key, item.Value);

        void IDictionary<string, StringValues>.Add(string key, StringValues value)
        {
            if (_isReadOnly)
            {
                ThrowHeadersReadOnlyException();
            }

            SetValue(key, ref value, false);
        }

        void ICollection<KeyValuePair<string, StringValues>>.Clear()
        {
            if (_isReadOnly)
            {
                ThrowHeadersReadOnlyException();
            }

            Clear();
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
            if (arrayIndex < 0)
            {
                ThrowArgumentException();
            }

            foreach (var headerKey in _headerKeyNames)
            {
                if (((_bits & (1L << headerKey.Value)) != 0))
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>(headerKey.Key, _headerData[headerKey.Value]);
                    ++arrayIndex;
                }
            }

            ((ICollection<KeyValuePair<string, StringValues>>)MaybeUnknown)?.CopyTo(array, arrayIndex);
        }

        protected abstract IEnumerator<KeyValuePair<string, StringValues>> GetEnumeratorFast();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumeratorFast();

        IEnumerator<KeyValuePair<string, StringValues>> IEnumerable<KeyValuePair<string, StringValues>>.GetEnumerator()
            => GetEnumeratorFast();

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
                ThrowHeadersReadOnlyException();
            }

            return RemoveFast(key);
        }

        bool IDictionary<string, StringValues>.TryGetValue(string key, out StringValues value)
            => TryGetValueFast(key, out value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ValidateHeaderCharacters(ref StringValues headerValues)
        {
            var count = headerValues.Count;
            for (var i = 0; i < count; i++)
            {
                ValidateHeaderCharacters(headerValues[i]);
            }
        }

        public static void ValidateHeaderCharacters(string headerCharacters)
        {
            if (headerCharacters != null)
            {
                foreach (var ch in headerCharacters)
                {
                    if (ch < 0x20 || ch > 0x7E)
                    {
                        ThrowInvalidHeaderCharacter(ch);
                    }
                }
            }
        }

        public static long ParseContentLength(ref StringValues value)
        {
            long parsed;
            if (!HeaderUtilities.TryParseInt64(value.ToString(), out parsed))
            {
                ThrowInvalidContentLengthException(value);
            }

            return parsed;
        }

        public static unsafe ConnectionOptions ParseConnection(StringValues connection)
        {
            var connectionOptions = ConnectionOptions.None;

            var connectionCount = connection.Count;
            for (var i = 0; i < connectionCount; i++)
            {
                var value = connection[i];
                fixed (char* ptr = value)
                {
                    var ch = ptr;
                    var tokenEnd = ch;
                    var end = ch + value.Length;

                    while (ch < end)
                    {
                        while (tokenEnd < end && *tokenEnd != ',')
                        {
                            tokenEnd++;
                        }

                        while (ch < tokenEnd && *ch == ' ')
                        {
                            ch++;
                        }

                        var tokenLength = tokenEnd - ch;

                        if (tokenLength >= 9 && (*ch | 0x20) == 'k')
                        {
                            if ((*++ch | 0x20) == 'e' &&
                                (*++ch | 0x20) == 'e' &&
                                (*++ch | 0x20) == 'p' &&
                                *++ch == '-' &&
                                (*++ch | 0x20) == 'a' &&
                                (*++ch | 0x20) == 'l' &&
                                (*++ch | 0x20) == 'i' &&
                                (*++ch | 0x20) == 'v' &&
                                (*++ch | 0x20) == 'e')
                            {
                                ch++;
                                while (ch < tokenEnd && *ch == ' ')
                                {
                                    ch++;
                                }

                                if (ch == tokenEnd)
                                {
                                    connectionOptions |= ConnectionOptions.KeepAlive;
                                }
                            }
                        }
                        else if (tokenLength >= 7 && (*ch | 0x20) == 'u')
                        {
                            if ((*++ch | 0x20) == 'p' &&
                                (*++ch | 0x20) == 'g' &&
                                (*++ch | 0x20) == 'r' &&
                                (*++ch | 0x20) == 'a' &&
                                (*++ch | 0x20) == 'd' &&
                                (*++ch | 0x20) == 'e')
                            {
                                ch++;
                                while (ch < tokenEnd && *ch == ' ')
                                {
                                    ch++;
                                }

                                if (ch == tokenEnd)
                                {
                                    connectionOptions |= ConnectionOptions.Upgrade;
                                }
                            }
                        }
                        else if (tokenLength >= 5 && (*ch | 0x20) == 'c')
                        {
                            if ((*++ch | 0x20) == 'l' &&
                                (*++ch | 0x20) == 'o' &&
                                (*++ch | 0x20) == 's' &&
                                (*++ch | 0x20) == 'e')
                            {
                                ch++;
                                while (ch < tokenEnd && *ch == ' ')
                                {
                                    ch++;
                                }

                                if (ch == tokenEnd)
                                {
                                    connectionOptions |= ConnectionOptions.Close;
                                }
                            }
                        }

                        tokenEnd++;
                        ch = tokenEnd;
                    }
                }
            }

            return connectionOptions;
        }

        public static unsafe TransferCoding GetFinalTransferCoding(StringValues transferEncoding)
        {
            var transferEncodingOptions = TransferCoding.None;

            var transferEncodingCount = transferEncoding.Count;
            for (var i = 0; i < transferEncodingCount; i++)
            {
                var value = transferEncoding[i];
                fixed (char* ptr = value)
                {
                    var ch = ptr;
                    var tokenEnd = ch;
                    var end = ch + value.Length;

                    while (ch < end)
                    {
                        while (tokenEnd < end && *tokenEnd != ',')
                        {
                            tokenEnd++;
                        }

                        while (ch < tokenEnd && *ch == ' ')
                        {
                            ch++;
                        }

                        var tokenLength = tokenEnd - ch;

                        if (tokenLength >= 7 && (*ch | 0x20) == 'c')
                        {
                            if ((*++ch | 0x20) == 'h' &&
                                (*++ch | 0x20) == 'u' &&
                                (*++ch | 0x20) == 'n' &&
                                (*++ch | 0x20) == 'k' &&
                                (*++ch | 0x20) == 'e' &&
                                (*++ch | 0x20) == 'd')
                            {
                                ch++;
                                while (ch < tokenEnd && *ch == ' ')
                                {
                                    ch++;
                                }

                                if (ch == tokenEnd)
                                {
                                    transferEncodingOptions = TransferCoding.Chunked;
                                }
                            }
                        }

                        if (tokenLength > 0 && ch != tokenEnd)
                        {
                            transferEncodingOptions = TransferCoding.Other;
                        }

                        tokenEnd++;
                        ch = tokenEnd;
                    }
                }
            }

            return transferEncodingOptions;
        }

        private static int BitCount(long value)
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

        protected void ThrowHeadersReadOnlyException()
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

        private static InvalidOperationException GetInvalidContentLengthException(string value)
        {
            return new InvalidOperationException($"Invalid Content-Length: \"{value}\". Value must be a positive integral number.");
        }

        private static void ThrowInvalidContentLengthException(string value)
        {
            throw GetInvalidContentLengthException(value);
        }

        private static InvalidOperationException GetInvalidHeaderCharacterException(char ch)
        {
            return new InvalidOperationException(string.Format("Invalid non-ASCII or control character in header: 0x{0:X4}", (ushort)ch));
        }

        private static void ThrowInvalidHeaderCharacter(char ch)
        {
            throw GetInvalidHeaderCharacterException(ch);
        }

        protected class HeaderKeyStringData
        {
            public readonly int Index;
            public readonly ulong[] MaskULongs;
            public readonly ulong[] CompULongs;
            public readonly uint MaskUInt;
            public readonly uint CompUInt;
            public readonly ushort MaskUShort;
            public readonly ushort CompUShort;

            public HeaderKeyStringData(int index, ulong[] maskULongs, ulong[] compULongs, uint maskUInt, uint compUInt, ushort maskUShort, ushort compUShort)
            {
                Index = index;
                MaskULongs = maskULongs;
                CompULongs = compULongs;
                MaskUInt = maskUInt;
                CompUInt = compUInt;
                MaskUShort = maskUShort;
                CompUShort = compUShort;
            }
        }
    }
}
