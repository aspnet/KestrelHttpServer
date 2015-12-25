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
        private static byte[] _CrLfColonSpace = new[] { (byte)'\r', (byte)'\n', (byte)':', (byte)' ' };

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        protected override IEnumerator<KeyValuePair<string, StringValues>> GetEnumeratorFast()
        {
            return GetEnumerator();
        }

        public unsafe void CopyTo(ref MemoryPoolIterator2 output)
        {
            CopyToFast(ref output);
            if (MaybeUnknown != null && MaybeUnknown.Count > 0)
            {
#if DOTNET5_4 || DNXCORE50
                fixed (byte* pCrLfColonSpace = _CrLfColonSpace)
                {
                    foreach (var kv in MaybeUnknown)
                    {
                        if (kv.Value.Count == 1)
                        {
                            var value = kv.Value[0];
                            if (value != null)
                            {
                                output.CopyFrom(pCrLfColonSpace, 2);
                                output.CopyFromAscii(kv.Key);
                                output.CopyFrom(pCrLfColonSpace + 2, 2);
                                output.CopyFromAscii(value);
                            }
                        }
                        else
                        {
                            foreach (var value in kv.Value)
                            {
                                if (value != null)
                                {
                                    output.CopyFrom(pCrLfColonSpace, 2);
                                    output.CopyFromAscii(kv.Key);
                                    output.CopyFrom(pCrLfColonSpace + 2, 2);
                                    output.CopyFromAscii(value);
                                }
                            }
                        }
                    }
                }
#else
                foreach (var kv in MaybeUnknown)
                {
                    if (kv.Value.Count == 1)
                    {
                        var value = kv.Value[0];
                        if (value != null)
                        {
                            output.CopyFrom(_CrLfColonSpace, 0, 2);
                            output.CopyFromAscii(kv.Key);
                            output.CopyFrom(_CrLfColonSpace, 2, 2);
                            output.CopyFromAscii(value);
                        }
                    }
                    else
                    {
                        foreach (var value in kv.Value)
                        {
                            if (value != null)
                            {
                                output.CopyFrom(_CrLfColonSpace, 0, 2);
                                output.CopyFromAscii(kv.Key);
                                output.CopyFrom(_CrLfColonSpace, 2, 2);
                                output.CopyFromAscii(value);
                            }
                        }
                    }
                }
#endif
            }
        }

        public partial struct Enumerator : IEnumerator<KeyValuePair<string, StringValues>>
        {
            private FrameResponseHeaders _collection;
            private long _bits;
            private int _state;
            private KeyValuePair<string, StringValues> _current;
            private bool _hasUnknown;
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
