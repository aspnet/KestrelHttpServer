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
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public abstract class FrameHeaders : IHeaderDictionary
    {
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
                    ThrowHeadersReadOnlyException();
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
                ThrowHeadersReadOnlyException();
            }
            AddValueFast(key, value);
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
                ThrowHeadersReadOnlyException();
            }
            return RemoveFast(key);
        }

        bool IDictionary<string, StringValues>.TryGetValue(string key, out StringValues value)
        {
            return TryGetValueFast(key, out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ValidateHeaderCharacters(ref StringValues headerValues)
        {
            var count = headerValues.Count;
            for (var i = 0; i < count; i++)
            {
                ValidateHeaderCharacters(headerValues[i]);
            }
        }

        public static unsafe void ValidateHeaderCharacters(string headerCharacters)
        {
            if (headerCharacters != null)
            {
                fixed (char* header = headerCharacters)
                {
                    // offsets and lengths are handled in byte* sizes
                    var pHeader = (byte*)header;
                    var offset = 0;
                    var length = headerCharacters.Length * 2;

                    if (Vector.IsHardwareAccelerated && Vector<byte>.Count <= length)
                    {
                        var vSub = Vector.AsVectorUInt16(new Vector<uint>(0x00200020u));
                        // 0x7e as highest ascii (don't include DEL)
                        // 0x20 as lowest ascii (space)
                        var vTest = Vector.AsVectorUInt16(new Vector<uint>(0x007e007eu - 0x00200020u));

                        do
                        {
                            var stringVector = Unsafe.Read<Vector<ushort>>(pHeader + offset);
                            offset += Vector<byte>.Count;
                            if (Vector.GreaterThanAny(stringVector - vSub, vTest))
                            {
                                ThrowInvalidHeaderCharacter(pHeader + offset, Vector<byte>.Count);
                            }
                        } while (offset <= length - Vector<byte>.Count);
                    }

                    // Non-vector testing:
                    // Flag 0x7f     => Add 0x0001 to each char so DEL (0x7f) will set a high bit
                    // Flag < 0x20   => Sub 0x0020 from each char so high bit will be set in previous char bit
                    // Flag > 0x007f => All but highest bit picked up by 0x7f flagging, highest bit picked up by < 0x20 flagging
                    // Bitwise | or the above three together
                    // Bitwise & and each char with 0xff80; result should be 0 if all tests pass
                    if (offset <= length - sizeof(ulong))
                    {
                        do
                        {
                            var stringUlong = (ulong*)(pHeader + offset);
                            offset += sizeof(ulong);
                            if ((((*stringUlong + 0x0001000100010001UL) | (*stringUlong - 0x0020002000200020UL)) & 0xff80ff80ff80ff80UL) != 0)
                            {
                                ThrowInvalidHeaderCharacter(pHeader + offset, sizeof(ulong));
                            }
                        } while (offset <= length - sizeof(ulong));
                    }
                    if (offset <= length - sizeof(uint))
                    {
                        var stringUint = (uint*)(pHeader + offset);
                        offset += sizeof(uint);
                        if ((((*stringUint + 0x00010001u) | (*stringUint - 0x00200020u)) & 0xff80ff80u) != 0)
                        {
                            ThrowInvalidHeaderCharacter(pHeader + offset, sizeof(uint));
                        }
                    }
                    if (offset <= length - sizeof(ushort))
                    {
                        var stringUshort = (ushort*)(pHeader + offset);
                        offset += sizeof(ushort);
                        if ((((*stringUshort + 0x0001u) | (*stringUshort - 0x0020u)) & 0xff80u) != 0)
                        {
                            ThrowInvalidHeaderCharacter(pHeader + offset, sizeof(ushort));
                        }
                    }
                }
            }
        }

        public static long ParseContentLength(StringValues value)
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

        private static void ThrowInvalidContentLengthException(string value)
        {
            throw new InvalidOperationException($"Invalid Content-Length: \"{value}\". Value must be a positive integral number.");
        }

        private unsafe static Exception GetInvalidHeaderCharacterException(byte* end, int byteCount)
        {
            var start = end - byteCount;
            while (start < end)
            {
                var ch = *(char*)start;
                if (ch < 0x20 || ch >= 0x7f)
                {
                    return new InvalidOperationException(string.Format("Invalid non-ASCII or control character in header: 0x{0:X4}", (ushort)ch));
                }
                start += sizeof(char);
            }

            // Should never be reached, use different exception type so unit tests can pick it up
            return new ArgumentException("Invalid non-ASCII or control character in header");
        }

        private unsafe static void ThrowInvalidHeaderCharacter(byte* end, int byteCount)
        {
            throw GetInvalidHeaderCharacterException(end, byteCount);
        }
    }
}
