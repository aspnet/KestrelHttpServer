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

        class KnownHeader
        {
            public string Name { get; set; }
            public int Index { get; set; }
            public string Identifier => Name.Replace("-", "");
            public byte[] Bytes => Encoding.ASCII.GetBytes($"\r\n{Name}: ");
            public bool EnhancedSetter { get; set; }
            public bool PrimaryHeader { get; set; }
            public string TestBit() => $"((_bits & (1L << (int)HeaderIndex.{Identifier})) != 0)";
            public string SetBit() => $"_bits |= (1L << (int)HeaderIndex.{Identifier})";

            public string BytesData()
            {
                string resultMaskUlong = null;
                string resultCompUlong = null;
                string resultMaskUint = "0";
                string resultCompUint = "0";
                string resultMaskUshort = "0";
                string resultCompUshort = "0";
                string resultMaskByte = "0";
                string resultCompByte = "0";
                var delim = "";
                var index = 0;
                while (index != Name.Length)
                {
                    if (Name.Length - index >= 8)
                    {
                        resultMaskUlong += delim + TermByteMask(Name, index, 8, "uL");
                        resultCompUlong += delim + TermByteComp(Name, index, 8, "uL");
                        index += 8;
                    }
                    else if (Name.Length - index >= 4)
                    {
                        resultMaskUint = TermByteMask(Name, index, 4, "u");
                        resultCompUint = TermByteComp(Name, index, 4, "u");
                        index += 4;
                    }
                    else if (Name.Length - index >= 2)
                    {
                        resultMaskUshort = "(ushort)" + TermByteMask(Name, index, 2, "u");
                        resultCompUshort = "(ushort)" + TermByteComp(Name, index, 2, "u");
                        index += 2;
                    }
                    else
                    {
                        resultMaskByte = "(byte)" + TermByteMask(Name, index, 1, "u");
                        resultCompByte = "(byte)" + TermByteComp(Name, index, 1, "u");
                        index += 1;
                    }
                    delim = ", ";
                }
                return $"{(resultMaskUlong != null ? "new ulong[] {" + resultMaskUlong + "}" : "ShortHeader")}," +
                       $"{(resultCompUlong != null ? "new ulong[] {" + resultCompUlong + "}" : "ShortHeader")}," +
                       $"{resultMaskUint}, {resultCompUint}, {resultMaskUshort}, {resultCompUshort}, {resultMaskByte}, {resultCompByte}";
            }
            public string CharsData()
            {
                string resultMaskUlong = null;
                string resultCompUlong = null;
                string resultMaskUint = "0";
                string resultCompUint = "0";
                string resultMaskUshort = "0";
                string resultCompUshort = "0";
                var delim = "";
                var index = 0;
                while (index != Name.Length)
                {
                    if (Name.Length - index >= 4)
                    {
                        resultMaskUlong += delim + TermCharMask(Name, index, 4, "uL");
                        resultCompUlong += delim + TermCharComp(Name, index, 4, "uL");
                        index += 4;
                    }
                    else if (Name.Length - index >= 2)
                    {
                        resultMaskUint = TermCharMask(Name, index, 2, "u");
                        resultCompUint = TermCharComp(Name, index, 2, "u");
                        index += 2;
                    }
                    else
                    {
                        resultMaskUshort = "(ushort)" + TermCharMask(Name, index, 1, "u");
                        resultCompUshort = "(ushort)" + TermCharComp(Name, index, 1, "u");
                        index += 1;
                    }
                    delim = ", ";
                }

                return $"{(resultMaskUlong != null ? "new ulong[] {" + resultMaskUlong + "}" : "ShortHeader")}, " +
                       $"{(resultCompUlong != null ? "new ulong[] {" + resultCompUlong + "}" : "ShortHeader")}, " +
                       $"{resultMaskUint}, {resultCompUint}, {resultMaskUshort}, {resultCompUshort}";
            }
            protected string TermByteComp(string name, int offset, int count, string suffix)
            {
                ulong comp = 0;
                for (var scan = 0; scan < count; scan++)
                {
                    var ch = (byte)name[offset + count - scan - 1];
                    var isAlpha = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
                    comp = (comp << 8) + (ch & (isAlpha ? 0xdfu : 0xffu));
                }
                return $"{comp}{suffix}";
            }
            protected string TermByteMask(string name, int offset, int count, string suffix)
            {
                ulong mask = 0;
                for (var scan = 0; scan < count; scan++)
                {
                    var ch = (byte)name[offset + count - scan - 1];
                    var isAlpha = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
                    mask = (mask << 8) + (isAlpha ? 0xdfu : 0xffu);
                }
                return $"{mask}{suffix}";
            }
            protected string TermCharComp(string name, int offset, int count, string suffix)
            {
                ulong comp = 0;
                for (var scan = 0; scan < count; scan++)
                {
                    var ch = (byte)name[offset + count - scan - 1];
                    var isAlpha = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
                    comp = (comp << 16) + (ch & (isAlpha ? 0xdfu : 0xffu));
                }
                return $"{comp}{suffix}";
            }
            protected string TermCharMask(string name, int offset, int count, string suffix)
            {
                ulong mask = 0;
                for (var scan = 0; scan < count; scan++)
                {
                    var ch = (byte)name[offset + count - scan - 1];
                    var isAlpha = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
                    mask = (mask << 16) + (isAlpha ? 0xffdfu : 0xffffu); // look at all 16 bits to validate
                }
                return $"{mask}{suffix}";
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
            }).Concat(corsRequestHeaders)
              .OrderBy(header => 
                        (requestPrimaryHeaders.Contains(header) ? "0" : "1") +
                        (commonHeaders.Contains(header) ? "0" : "1"))
              .Select((header, index) => new KnownHeader
                {
                    Name = header,
                    Index = index,
                    PrimaryHeader = requestPrimaryHeaders.Contains(header)
                }).ToArray();
            var enhancedHeaders = new[]
            {
                "Connection",
                "Server",
                "Date",
                "Transfer-Encoding",
                "Content-Length",
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
            }).Concat(corsResponseHeaders)
              .OrderBy(header =>
                        (responsePrimaryHeaders.Contains(header) ? "0" : "1") +
                        (enhancedHeaders.Contains(header) ? "0" : "1") +
                        (commonHeaders.Contains(header) ? "0" : "1") +
                        (corsResponseHeaders.Contains(header) ? "0" : "1"))
              .Select((header, index) => new KnownHeader
            {
                Name = header,
                Index = index,
                EnhancedSetter = enhancedHeaders.Contains(header),
                PrimaryHeader = responsePrimaryHeaders.Contains(header)
            }).ToArray();
            var loops = new[]
            {
                new
                {
                    Headers = requestHeaders,
                    HeadersByLength = requestHeaders.GroupBy(x => x.Name.Length),
                    ClassName = "FrameRequestHeaders"
                },
                new
                {
                    Headers = responseHeaders,
                    HeadersByLength = responseHeaders.GroupBy(x => x.Name.Length),
                    ClassName = "FrameResponseHeaders"
                }
            };

            return $@"// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{{{Each(loops, loop => $@"
    public partial class {loop.ClassName}
    {{{If(loop.ClassName == "FrameResponseHeaders", () =>
        $@"
        private readonly static byte[][] _keyBytes = new byte[][]
        {{{Each(loop.Headers, header => $@"
            new byte[]{{{Each(header.Bytes, b => $"{b},")}}},")}
        }};
")}
        private static readonly HeaderKeyStringData[][] _keyStringDataByLength = new HeaderKeyStringData[][]
        {{{Each(Enumerable.Range(0, loop.HeadersByLength.Max((byLength) => byLength.Key) + 1), length => $@"{(loop.HeadersByLength.FirstOrDefault(byLength => byLength.Key == length) == null ? @"
            NoHeaders," : $@"
            new HeaderKeyStringData[] {{{Each(loop.HeadersByLength.FirstOrDefault(byLength => byLength.Key == length), header => $@"
                new HeaderKeyStringData((int)HeaderIndex.{header.Identifier}, {header.CharsData()}),")}}},")}")}
        }};
{If(loop.ClassName == "FrameRequestHeaders", () =>
        $@"
        private static readonly HeaderKeyByteData[] NoRequestHeaders = new HeaderKeyByteData[0];
        private static readonly HeaderKeyByteData[][] _keyByteDataByLength = new HeaderKeyByteData[][]
        {{{Each(Enumerable.Range(0, loop.HeadersByLength.Max((byLength) => byLength.Key) + 1), length => $@"{(loop.HeadersByLength.FirstOrDefault(byLength => byLength.Key == length) == null ? @"
            NoRequestHeaders," : $@"
            new HeaderKeyByteData[] {{{Each(loop.HeadersByLength.FirstOrDefault(byLength => byLength.Key == length), header => $@"
                new HeaderKeyByteData((int)HeaderIndex.{header.Identifier}, {header.BytesData()}),")}}},")}")}
        }};
")}
        private static readonly KeyValuePair<string, int>[] HeaderNames = new []
        {{{Each(loop.Headers, (header) => $@"
            new KeyValuePair<string, int>(""{header.Name}"", (int)HeaderIndex.{header.Identifier}),")}
        }};{If(loop.Headers.Any(header => header.EnhancedSetter), () => @"
")}{Each(loop.Headers.Where(header => header.EnhancedSetter), header => @"
        public byte[] _raw" + header.Identifier + ";") + @"
"}
        public {loop.ClassName}() : base(HeaderNames, _keyStringDataByLength, new StringValues[{loop.Headers.Length:0}])
        {{
        }}
{Each(loop.Headers, header => $@"
        public StringValues Header{header.Identifier}
        {{
            get
            {{
                if ({header.TestBit()})
                {{
                    return _headerData[(int)HeaderIndex.{header.Identifier}];
                }}
                return StringValues.Empty;
            }}
            set
            {{{If(loop.ClassName == "FrameResponseHeaders" && header.Identifier == "ContentLength", () => @"
                _contentLength = ParseContentLength(ref value);")}
                {header.SetBit()};
                _headerData[(int)HeaderIndex.{header.Identifier}] = value; {(header.EnhancedSetter == false ? "" : $@"
                _raw{header.Identifier} = null;")}
            }}
        }}
")}{Each(loop.Headers.Where(header => header.EnhancedSetter), header => $@"
        public void SetRaw{header.Identifier}(StringValues value, byte[] raw)
        {{{If(loop.ClassName == "FrameResponseHeaders" && header.Identifier == "ContentLength", () => @"
            _contentLength = ParseContentLength(ref value);")}
            {header.SetBit()};
            _headerData[(int)HeaderIndex.{header.Identifier}] = value;
            _raw{header.Identifier} = raw;
        }}
")}{If(loop.ClassName == "FrameResponseHeaders", () => $@"
        protected override void ClearExtra(int index)
        {{
            switch (index)
            {{{Each(loop.Headers.Where(header => header.EnhancedSetter).OrderBy(header => header.Index), header => $@"
                case (int)HeaderIndex.{header.Identifier}:{If(header.Identifier == "ContentLength", () => @"
                    _contentLength = null;")} 
                    _raw{header.Identifier} = null;
                    break;")}
            }}
        }}
")}{If(loop.ClassName == "FrameResponseHeaders", () => $@"
        public void CopyTo(ref MemoryPoolIterator output)
        {{
            var bits = _bits;
            _bits = 0;
            var flag = 1L;
            var headers = _headerData;
            for (var h = 0; h < headers.Length; h++)
            {{
                var hasHeader = (bits & flag) != 0;
                flag = 1L << (h + 1);

                if (!hasHeader)
                {{
                    continue;
                }}

                switch (h)
                {{{Each(loop.Headers.Where(header => header.EnhancedSetter || header.Identifier == "ContentLength").OrderBy(header => header.Index), header => $@"
                    case (int)HeaderIndex.{header.Identifier}:
                        if (_raw{header.Identifier} != null)
                        {{
                            output.CopyFrom(_raw{header.Identifier});
                            _raw{header.Identifier} = null;
                            continue;
                        }}
                        break;")}
                }}

                var values = _headerData[h];
                _headerData[h] = default(StringValues);
                var valueCount = values.Count;
                for (var v = 0; v < valueCount; v++)
                {{
                    var value = values[v];
                    if (value != null)
                    {{
                        output.CopyFrom(_keyBytes[h]);
                        output.CopyFromAscii(value);
                    }}
                }}

                if (bits < flag)
                {{
                    break;
                }}
            }}

            if (MaybeUnknown != null)
            {{
                CopyExtraTo(ref output);
            }}
        }}
")}
        private enum HeaderIndex
        {{{Each(loop.Headers, (header) => $@"
            {header.Identifier} = {header.Index},")}
        }}
    }}
")}}}";
        }
    }
}