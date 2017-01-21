using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.AspNetCore.Server.Kestrel.GeneratedCode
{
    // This project can output the Class library as a NuGet Package.
    // To enable this option, right-click on the project and select the Properties menu item. In the Build tab select "Produce outputs on build".
    public class KnownHeaders
    {
        static string Each<T>(IEnumerable<T> values, Func<T, string> formatter)
        {
            return values.Any() ? values.Select(formatter).Aggregate((a, b) => a + b) : "";
        }

        static string If(bool condition, Func<string> formatter)
        {
            return condition ? formatter() : "";
        }

        static string AppendSwitch(IEnumerable<IGrouping<int, KnownHeader>> values, string className, bool handleUnknown = false) => 
            $@"fixed (byte* ptr = &keyBytes[keyOffset])
            {{
                var pUB = ptr;
                var pUL = (ulong*)pUB;
                var pUI = (uint*)pUB;
                var pUS = (ushort*)pUB;
                switch (keyLength)
                {{{Each(values, byLength => $@"
                    case {byLength.Key}:
                        {{{Each(byLength, header => $@"
                            if ({header.EqualIgnoreCaseBytes()})
                            {{{(header.Identifier == "ContentLength" ? $@"
                                if (ContentLength.HasValue)
                                {{
                                    ThrowRequestMultipleContentLengths();
                                }}
                                else
                                {{
                                    ContentLength = ParseRequestContentLength(value);
                                }}
                                return;" : $@"
                                if ({header.TestBit()})
                                {{
                                    _headers._{header.Identifier} = AppendValue(_headers._{header.Identifier}, value);
                                }}
                                else
                                {{
                                    {header.SetBit()};
                                    _headers._{header.Identifier} = new StringValues(value);{(header.EnhancedSetter == false ? "" : $@"
                                    _headers._raw{header.Identifier} = null;")}
                                }}
                                return;")}
                            }}
                        ")}}}
                        break;
                ")}}}

                {(handleUnknown ? $@"
                    key = new string('\0', keyLength);
                    fixed(char *keyBuffer = key)
                    {{
                        if (!AsciiUtilities.TryGetAsciiString(ptr, keyBuffer, keyLength))
                        {{
                            throw BadHttpRequestException.GetException(RequestRejectionReason.InvalidCharactersInHeaderName);
                        }}
                    }}
                ": "")}
            }}";

        class KnownHeader
        {
            public string Name { get; set; }
            public int Index { get; set; }
            public string Identifier => Name.Replace("-", "");

            public byte[] Bytes => Encoding.ASCII.GetBytes($"\r\n{Name}: ");
            public int BytesOffset { get; set; }
            public int BytesCount { get; set; }
            public bool EnhancedSetter { get; set; }
            public bool PrimaryHeader { get; set; }
            public string TestBit() => $"((_bits & {1L << Index}L) != 0)";
            public string SetBit() => $"_bits |= {1L << Index}L";
            public string ClearBit() => $"_bits &= ~{1L << Index}L";
            
            public string EqualIgnoreCaseBytes()
            {
                var result = "";
                var delim = "";
                var index = 0;
                while (index != Name.Length)
                {
                    if (Name.Length - index >= 8)
                    {
                        result += delim + Term(Name, index, 8, "pUL", "uL");
                        index += 8;
                    }
                    else if (Name.Length - index >= 4)
                    {
                        result += delim + Term(Name, index, 4, "pUI", "u");
                        index += 4;
                    }
                    else if (Name.Length - index >= 2)
                    {
                        result += delim + Term(Name, index, 2, "pUS", "u");
                        index += 2;
                    }
                    else
                    {
                        result += delim + Term(Name, index, 1, "pUB", "u");
                        index += 1;
                    }
                    delim = " && ";
                }
                return $"({result})";
            }
            protected string Term(string name, int offset, int count, string array, string suffix)
            {
                ulong mask = 0;
                ulong comp = 0;
                for (var scan = 0; scan < count; scan++)
                {
                    var ch = (byte)name[offset + count - scan - 1];
                    var isAlpha = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
                    comp = (comp << 8) + (ch & (isAlpha ? 0xdfu : 0xffu));
                    mask = (mask << 8) + (isAlpha ? 0xdfu : 0xffu);
                }
                return $"(({array}[{offset / count}] & {mask}{suffix}) == {comp}{suffix})";
            }
        }

        public static string GeneratedFile()
        {
            var requestPrimaryHeaders = new[]
            {
                "Accept",
                "Host",
                "User-Agent"

            };
            var responsePrimaryHeaders = new[]
            {
                "Connection",
                "Date",
                "Content-Length",
                "Content-Type",
                "Server",
            };
            var commonHeaders = new[]
            {
                "Cache-Control",
                "Connection",
                "Date",
                "Keep-Alive",
                "Pragma",
                "Trailer",
                "Transfer-Encoding",
                "Upgrade",
                "Via",
                "Warning",
                "Allow",
                "Content-Length",
                "Content-Type",
                "Content-Encoding",
                "Content-Language",
                "Content-Location",
                "Content-MD5",
                "Content-Range",
                "Expires",
                "Last-Modified"
            };
            // http://www.w3.org/TR/cors/#syntax
            var corsRequestHeaders = new[]
            {
                "Origin",
                "Access-Control-Request-Method",
                "Access-Control-Request-Headers",
            };
            var requestHeaders = commonHeaders.Concat(new[]
            {
                "Accept",
                "Accept-Charset",
                "Accept-Encoding",
                "Accept-Language",
                "Authorization",
                "Cookie",
                "Expect",
                "From",
                "Host",
                "If-Match",
                "If-Modified-Since",
                "If-None-Match",
                "If-Range",
                "If-Unmodified-Since",
                "Max-Forwards",
                "Proxy-Authorization",
                "Referer",
                "Range",
                "TE",
                "Translate",
                "User-Agent",
            })
            .Concat(corsRequestHeaders)
            .Where((header) => header != "Content-Length")
            .Select((header, index) => new KnownHeader
            {
                Name = header,
                Index = index,
                PrimaryHeader = requestPrimaryHeaders.Contains(header)
            })
            .Concat(new[] { new KnownHeader
            {
                Name = "Content-Length",
                Index = -1,
                PrimaryHeader = requestPrimaryHeaders.Contains("Content-Length")
            }})
            .ToArray();
            var enhancedHeaders = new[]
            {
                "Connection",
                "Server",
                "Date",
                "Transfer-Encoding"
            };
            // http://www.w3.org/TR/cors/#syntax
            var corsResponseHeaders = new[]
            {
                "Access-Control-Allow-Credentials",
                "Access-Control-Allow-Headers",
                "Access-Control-Allow-Methods",
                "Access-Control-Allow-Origin",
                "Access-Control-Expose-Headers",
                "Access-Control-Max-Age",
            };
            var responseHeaders = commonHeaders.Concat(new[]
            {
                "Accept-Ranges",
                "Age",
                "ETag",
                "Location",
                "Proxy-Authenticate",
                "Retry-After",
                "Server",
                "Set-Cookie",
                "Vary",
                "WWW-Authenticate",
            })
            .Concat(corsResponseHeaders)
            .Where((header) => header != "Content-Length")
            .Select((header, index) => new KnownHeader
            {
                Name = header,
                Index = index,
                EnhancedSetter = enhancedHeaders.Contains(header),
                PrimaryHeader = responsePrimaryHeaders.Contains(header)
            })
            .Concat(new[] { new KnownHeader
            {
                Name = "Content-Length",
                Index = -1,
                EnhancedSetter = enhancedHeaders.Contains("Content-Length"),
                PrimaryHeader = responsePrimaryHeaders.Contains("Content-Length")
            }})
            .ToArray();
            var loops = new[]
            {
                new
                {
                    Headers = requestHeaders,
                    HeadersByLength = requestHeaders.GroupBy(x => x.Name.Length),
                    ClassName = "FrameRequestHeaders",
                    Bytes = default(byte[])
                },
                new
                {
                    Headers = responseHeaders,
                    HeadersByLength = responseHeaders.GroupBy(x => x.Name.Length),
                    ClassName = "FrameResponseHeaders",
                    Bytes = responseHeaders.SelectMany(header => header.Bytes).ToArray()
                }
            };
            foreach (var loop in loops.Where(l => l.Bytes != null))
            {
                var offset = 0;
                foreach (var header in loop.Headers)
                {
                    header.BytesOffset = offset;
                    header.BytesCount += header.Bytes.Length;
                    offset += header.BytesCount;
                }
            }
            return $@"// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{{
{Each(loops, loop => $@"
    public partial class {loop.ClassName}
    {{{(loop.Bytes != null ?
        $@"
        private static byte[] _headerBytes = new byte[]
        {{
            {Each(loop.Bytes, b => $"{b},")}
        }};"
        : "")}

        private long _bits = 0;
        private HeaderReferences _headers;
        {Each(loop.Headers, header => $@"
        public StringValues Header{header.Identifier}
        {{{(header.Identifier == "ContentLength" ? $@"
            get
            {{
                if (ContentLength.HasValue)
                {{
                    return HeaderUtilities.FormatInt64(ContentLength.Value);
                }}
                return StringValues.Empty;
            }}
            set
            {{
                ContentLength = Parse{(loop.ClassName == "FrameResponseHeaders" ? "Response" : "Request")}ContentLength(value);
            }}" : $@"
            get
            {{
                if ({header.TestBit()})
                {{
                    return _headers._{header.Identifier};
                }}
                return StringValues.Empty;
            }}
            set
            {{
                {header.SetBit()};
                _headers._{header.Identifier} = value; {(header.EnhancedSetter == false ? "" : $@"
                _headers._raw{header.Identifier} = null;")}
            }}")}
        }}")}
        {Each(loop.Headers.Where(header => header.EnhancedSetter), header => $@"
        public void SetRaw{header.Identifier}(StringValues value, byte[] raw)
        {{
            {header.SetBit()};
            _headers._{header.Identifier} = value;
            _headers._raw{header.Identifier} = raw;
        }}")}
        protected override int GetCountFast()
        {{
            return (ContentLength.HasValue ? 1 : 0 ) + BitCount(_bits) + (MaybeUnknown?.Count ?? 0);
        }}
        protected override StringValues GetValueFast(string key)
        {{
            switch (key.Length)
            {{{Each(loop.HeadersByLength, byLength => $@"
                case {byLength.Key}:
                    {{{Each(byLength, header => $@"
                        if (""{header.Name}"".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {{{(header.Identifier == "ContentLength" ? @"
                            if (ContentLength.HasValue)
                            {
                                return HeaderUtilities.FormatInt64(ContentLength.Value);
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }" : $@"
                            if ({header.TestBit()})
                            {{
                                return _headers._{header.Identifier};
                            }}
                            else
                            {{
                                ThrowKeyNotFoundException();
                            }}")}
                        }}
                    ")}}}
                    break;")}
            }}
            if (MaybeUnknown == null)
            {{
                ThrowKeyNotFoundException();
            }}
            return MaybeUnknown[key];
        }}
        protected override bool TryGetValueFast(string key, out StringValues value)
        {{
            switch (key.Length)
            {{{Each(loop.HeadersByLength, byLength => $@"
                case {byLength.Key}:
                    {{{Each(byLength, header => $@"
                        if (""{header.Name}"".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {{{(header.Identifier == "ContentLength" ? @"
                            if (ContentLength.HasValue)
                            {
                                value = HeaderUtilities.FormatInt64(ContentLength.Value);
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }" : $@"
                            if ({header.TestBit()})
                            {{
                                value = _headers._{header.Identifier};
                                return true;
                            }}
                            else
                            {{
                                value = StringValues.Empty;
                                return false;
                            }}")}
                        }}
                    ")}}}
                    break;
")}}}
            value = StringValues.Empty;
            return MaybeUnknown?.TryGetValue(key, out value) ?? false;
        }}
        protected override void SetValueFast(string key, StringValues value)
        {{
            {(loop.ClassName == "FrameResponseHeaders" ? "ValidateHeaderCharacters(value);" : "")}
            switch (key.Length)
            {{{Each(loop.HeadersByLength, byLength => $@"
                case {byLength.Key}:
                    {{{Each(byLength, header => $@"
                        if (""{header.Name}"".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {{{(header.Identifier == "ContentLength" ? $@"
                            ContentLength = Parse{(loop.ClassName == "FrameResponseHeaders" ? "Response" : "Request")}ContentLength(value);" : $@"
                            {header.SetBit()};
                            _headers._{header.Identifier} = value;{(header.EnhancedSetter == false ? "" : $@"
                            _headers._raw{header.Identifier} = null;")}")}
                            return;
                        }}
                    ")}}}
                    break;
")}}}
            {(loop.ClassName == "FrameResponseHeaders" ? "ValidateHeaderCharacters(key);" : "")}
            Unknown[key] = value;
        }}
        protected override void AddValueFast(string key, StringValues value)
        {{
            {(loop.ClassName == "FrameResponseHeaders" ? "ValidateHeaderCharacters(value);" : "")}
            switch (key.Length)
            {{{Each(loop.HeadersByLength, byLength => $@"
                case {byLength.Key}:
                    {{{Each(byLength, header => $@"
                        if (""{header.Name}"".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {{{(header.Identifier == "ContentLength" ? $@"
                            if (ContentLength.HasValue)
                            {{
                                ThrowDuplicateKeyException();
                            }}
                            else
                            {{
                                ContentLength = Parse{(loop.ClassName == "FrameResponseHeaders" ? "Response" : "Request")}ContentLength(value);
                            }}" : $@"
                            if ({header.TestBit()})
                            {{
                                ThrowDuplicateKeyException();
                            }}
                            {header.SetBit()};
                            _headers._{header.Identifier} = value;{(header.EnhancedSetter == false ? "" : $@"
                            _headers._raw{header.Identifier} = null;")}")}
                            return;
                        }}
                    ")}}}
                    break;
            ")}}}
            {(loop.ClassName == "FrameResponseHeaders" ? "ValidateHeaderCharacters(key);" : "")}
            Unknown.Add(key, value);
        }}
        protected override bool RemoveFast(string key)
        {{
            switch (key.Length)
            {{{Each(loop.HeadersByLength, byLength => $@"
                case {byLength.Key}:
                    {{{Each(byLength, header => $@"
                        if (""{header.Name}"".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {{{(header.Identifier == "ContentLength" ? @"
                            if (ContentLength.HasValue)
                            {
                                ContentLength = null;
                                return true;
                            }
                            else
                            {
                                return false;
                            }" : $@"
                            if ({header.TestBit()})
                            {{
                                {header.ClearBit()};
                                _headers._{header.Identifier} = StringValues.Empty;{(header.EnhancedSetter == false ? "" : $@"
                                _headers._raw{header.Identifier} = null;")}
                                return true;
                            }}
                            else
                            {{
                                return false;
                            }}")}
                        }}
                    ")}}}
                    break;
            ")}}}
            return MaybeUnknown?.Remove(key) ?? false;
        }}
        protected override void ClearFast()
        {{            
            MaybeUnknown?.Clear();
            ContentLength = null;
            if(FrameHeaders.BitCount(_bits) > 11)
            {{
                _headers = default(HeaderReferences);
                _bits = 0;
                return;
            }}
            {Each(loop.Headers.Where(header => header.Index >= 0).OrderBy(h => !h.PrimaryHeader), header => $@"
            if ({header.TestBit()})
            {{
                _headers._{header.Identifier} = default(StringValues);
                {header.ClearBit()};
                if(_bits == 0)
                {{
                    return;
                }}
            }}
            ")}
        }}

        protected override void CopyToFast(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {{
            if (arrayIndex < 0)
            {{
                ThrowArgumentException();
            }}
            {Each(loop.Headers.Where(header => header.Index >= 0), header => $@"
                if ({header.TestBit()})
                {{
                    if (arrayIndex == array.Length)
                    {{
                        ThrowArgumentException();
                    }}

                    array[arrayIndex] = new KeyValuePair<string, StringValues>(""{header.Name}"", _headers._{header.Identifier});
                    ++arrayIndex;
                }}")}
                if (ContentLength.HasValue)
                {{
                    if (arrayIndex == array.Length)
                    {{
                        ThrowArgumentException();
                    }}

                    array[arrayIndex] = new KeyValuePair<string, StringValues>(""Content-Length"", HeaderUtilities.FormatInt64(ContentLength.Value));
                    ++arrayIndex;
                }}
            ((ICollection<KeyValuePair<string, StringValues>>)MaybeUnknown)?.CopyTo(array, arrayIndex);
        }}
        {(loop.ClassName == "FrameResponseHeaders" ? $@"
        protected void CopyToFast(ref MemoryPoolIterator output)
        {{
            var tempBits = _bits | (ContentLength.HasValue ? {1L << 63}L : 0);
            {Each(loop.Headers.OrderBy(h => !h.PrimaryHeader), header => $@"
                {(header.Identifier == "ContentLength" ? $@"
                if (ContentLength.HasValue)
                {{
                    output.CopyFrom(_headerBytes, {header.BytesOffset}, {header.BytesCount});
                    output.CopyFromNumeric((ulong)ContentLength.Value);

                    tempBits &= ~{1L << 63}L;
                    if(tempBits == 0)
                    {{
                        return;
                    }}
                }}" : $@"
                if ({header.TestBit()})
                {{ {(header.EnhancedSetter == false ? "" : $@"
                    if (_headers._raw{header.Identifier} != null)
                    {{
                        output.CopyFrom(_headers._raw{header.Identifier}, 0, _headers._raw{header.Identifier}.Length);
                    }}
                    else ")}
                    {{
                        var valueCount = _headers._{header.Identifier}.Count; 
                        for (var i = 0; i < valueCount; i++) 
                        {{
                            var value = _headers._{header.Identifier}[i]; 
                            if (value != null)
                            {{
                                output.CopyFrom(_headerBytes, {header.BytesOffset}, {header.BytesCount});
                                output.CopyFromAscii(value);
                            }}
                        }}
                    }}

                    tempBits &= ~{1L << header.Index}L;
                    if(tempBits == 0)
                    {{
                        return;
                    }}
                }}")}
            ")}
        }}
        
        " : "")}
        {(loop.ClassName == "FrameRequestHeaders" ? $@"
        public unsafe void Append(byte[] keyBytes, int keyOffset, int keyLength, string value)
        {{
            {AppendSwitch(loop.Headers.Where(h => h.PrimaryHeader).GroupBy(x => x.Name.Length), loop.ClassName)}
            
            AppendNonPrimaryHeaders(keyBytes, keyOffset, keyLength, value);
        }}
        
        private unsafe void AppendNonPrimaryHeaders(byte[] keyBytes, int keyOffset, int keyLength, string value)
        {{
            string key;
            {AppendSwitch(loop.Headers.Where(h => !h.PrimaryHeader).GroupBy(x => x.Name.Length), loop.ClassName, true)}

            StringValues existing;
            Unknown.TryGetValue(key, out existing);
            Unknown[key] = AppendValue(existing, value);
        }}" : "")}
        private struct HeaderReferences
        {{{Each(loop.Headers.Where(header => header.Index >= 0), header => @"
            public StringValues _" + header.Identifier + ";")}
            {Each(loop.Headers.Where(header => header.EnhancedSetter), header => @"
            public byte[] _raw" + header.Identifier + ";")}
        }}

        public partial struct Enumerator
        {{
            public bool MoveNext()
            {{
                switch (_state)
                {{
                    {Each(loop.Headers.Where(header => header.Index >= 0), header => $@"
                    case {header.Index}:
                        goto state{header.Index};
                    ")}
                    case {loop.Headers.Count()}:
                        goto state{loop.Headers.Count()};
                    default:
                        goto state_default;
                }}
                {Each(loop.Headers.Where(header => header.Index >= 0), header => $@"
                state{header.Index}:
                    if ({header.TestBit()})
                    {{
                        _current = new KeyValuePair<string, StringValues>(""{header.Name}"", _collection._headers._{header.Identifier});
                        _state = {header.Index + 1};
                        return true;
                    }}
                ")}
                state{loop.Headers.Count()}:
                    if (_collection.ContentLength.HasValue)
                    {{
                        _current = new KeyValuePair<string, StringValues>(""Content-Length"", HeaderUtilities.FormatInt64(_collection.ContentLength.Value));
                        _state = {loop.Headers.Count() + 1};
                        return true;
                    }}
                state_default:
                    if (!_hasUnknown || !_unknownEnumerator.MoveNext())
                    {{
                        _current = default(KeyValuePair<string, StringValues>);
                        return false;
                    }}
                    _current = _unknownEnumerator.Current;
                    return true;
            }}
        }}
    }}
")}}}";
        }
    }
}