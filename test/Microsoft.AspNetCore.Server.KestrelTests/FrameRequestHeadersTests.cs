// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class FrameRequestHeadersTests
    {
        [Fact]
        public void InitialDictionaryIsEmpty()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();

            Assert.Equal(0, headers.Count);
            Assert.False(headers.IsReadOnly);
        }

        [Fact]
        public void SettingUnknownHeadersWorks()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();

            headers["custom"] = new[] { "value" };

            Assert.NotNull(headers["custom"]);
            Assert.Equal(1, headers["custom"].Count);
            Assert.Equal("value", headers["custom"][0]);
        }

        [Fact]
        public void SettingKnownHeadersWorks()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();

            headers["host"] = new[] { "value" };
            headers["content-length"] = new[] { "0" };

            Assert.NotNull(headers["host"]);
            Assert.NotNull(headers["content-length"]);
            Assert.Equal(1, headers["host"].Count);
            Assert.Equal(1, headers["content-length"].Count);
            Assert.Equal("value", headers["host"][0]);
            Assert.Equal("0", headers["content-length"][0]);
        }

        [Fact]
        public void KnownAndCustomHeaderCountAddedTogether()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();

            headers["host"] = new[] { "value" };
            headers["custom"] = new[] { "value" };
            headers["Content-Length"] = new[] { "0" };

            Assert.Equal(3, headers.Count);
        }

        [Fact]
        public void TryGetValueWorksForKnownAndUnknownHeaders()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();

            StringValues value;
            Assert.False(headers.TryGetValue("host", out value));
            Assert.False(headers.TryGetValue("custom", out value));
            Assert.False(headers.TryGetValue("Content-Length", out value));

            headers["host"] = new[] { "value" };
            Assert.True(headers.TryGetValue("host", out value));
            Assert.False(headers.TryGetValue("custom", out value));
            Assert.False(headers.TryGetValue("Content-Length", out value));

            headers["custom"] = new[] { "value" };
            Assert.True(headers.TryGetValue("host", out value));
            Assert.True(headers.TryGetValue("custom", out value));
            Assert.False(headers.TryGetValue("Content-Length", out value));

            headers["Content-Length"] = new[] { "0" };
            Assert.True(headers.TryGetValue("host", out value));
            Assert.True(headers.TryGetValue("custom", out value));
            Assert.True(headers.TryGetValue("Content-Length", out value));
        }

        [Fact]
        public void SameExceptionThrownForMissingKey()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();

            Assert.Throws<KeyNotFoundException>(() => headers["custom"]);
            Assert.Throws<KeyNotFoundException>(() => headers["host"]);
            Assert.Throws<KeyNotFoundException>(() => headers["Content-Length"]);
        }

        [Fact]
        public void EntriesCanBeEnumerated()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();
            var v1 = new[] { "localhost" };
            var v2 = new[] { "0" };
            var v3 = new[] { "value" };
            headers["host"] = v1;
            headers["Content-Length"] = v2;
            headers["custom"] = v3;

            Assert.Equal(
                new[] {
                    new KeyValuePair<string, StringValues>("Host", v1),
                    new KeyValuePair<string, StringValues>("Content-Length", v2),
                    new KeyValuePair<string, StringValues>("custom", v3),
                },
                headers);
        }

        [Fact]
        public void KeysAndValuesCanBeEnumerated()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();
            StringValues v1 = new[] { "localhost" };
            StringValues v2 = new[] { "0" };
            StringValues v3 = new[] { "value" };
            headers["host"] = v1;
            headers["Content-Length"] = v2;
            headers["custom"] = v3;

            Assert.Equal<string>(
                new[] { "Host", "Content-Length", "custom" },
                headers.Keys);

            Assert.Equal<StringValues>(
                new[] { v1, v2, v3 },
                headers.Values);
        }

        [Fact]
        public void ContainsAndContainsKeyWork()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();
            var kv1 = new KeyValuePair<string, StringValues>("host", new[] { "localhost" });
            var kv2 = new KeyValuePair<string, StringValues>("custom", new[] { "value" });
            var kv3 = new KeyValuePair<string, StringValues>("Content-Length", new[] { "0" });
            var kv1b = new KeyValuePair<string, StringValues>("host", new[] { "not-localhost" });
            var kv2b = new KeyValuePair<string, StringValues>("custom", new[] { "not-value" });
            var kv3b = new KeyValuePair<string, StringValues>("Content-Length", new[] { "1" });

            Assert.False(headers.ContainsKey("host"));
            Assert.False(headers.ContainsKey("custom"));
            Assert.False(headers.ContainsKey("Content-Length"));
            Assert.False(headers.Contains(kv1));
            Assert.False(headers.Contains(kv2));
            Assert.False(headers.Contains(kv3));

            headers["host"] = kv1.Value;
            Assert.True(headers.ContainsKey("host"));
            Assert.False(headers.ContainsKey("custom"));
            Assert.False(headers.ContainsKey("Content-Length"));
            Assert.True(headers.Contains(kv1));
            Assert.False(headers.Contains(kv2));
            Assert.False(headers.Contains(kv3));
            Assert.False(headers.Contains(kv1b));
            Assert.False(headers.Contains(kv2b));
            Assert.False(headers.Contains(kv3b));

            headers["custom"] = kv2.Value;
            Assert.True(headers.ContainsKey("host"));
            Assert.True(headers.ContainsKey("custom"));
            Assert.False(headers.ContainsKey("Content-Length"));
            Assert.True(headers.Contains(kv1));
            Assert.True(headers.Contains(kv2));
            Assert.False(headers.Contains(kv3));
            Assert.False(headers.Contains(kv1b));
            Assert.False(headers.Contains(kv2b));
            Assert.False(headers.Contains(kv3b));

            headers["Content-Length"] = kv3.Value;
            Assert.True(headers.ContainsKey("host"));
            Assert.True(headers.ContainsKey("custom"));
            Assert.True(headers.ContainsKey("Content-Length"));
            Assert.True(headers.Contains(kv1));
            Assert.True(headers.Contains(kv2));
            Assert.True(headers.Contains(kv3));
            Assert.False(headers.Contains(kv1b));
            Assert.False(headers.Contains(kv2b));
            Assert.False(headers.Contains(kv3b));
        }

        [Fact]
        public void AddWorksLikeSetAndThrowsIfKeyExists()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();

            StringValues value;
            Assert.False(headers.TryGetValue("host", out value));
            Assert.False(headers.TryGetValue("custom", out value));
            Assert.False(headers.TryGetValue("Content-Length", out value));

            headers.Add("host", new[] { "localhost" });
            headers.Add("custom", new[] { "value" });
            headers.Add("Content-Length", new[] { "0" });
            Assert.True(headers.TryGetValue("host", out value));
            Assert.True(headers.TryGetValue("custom", out value));
            Assert.True(headers.TryGetValue("Content-Length", out value));

            Assert.Throws<ArgumentException>(() => headers.Add("host", new[] { "localhost" }));
            Assert.Throws<ArgumentException>(() => headers.Add("custom", new[] { "value" }));
            Assert.Throws<ArgumentException>(() => headers.Add("Content-Length", new[] { "0" }));
            Assert.True(headers.TryGetValue("host", out value));
            Assert.True(headers.TryGetValue("custom", out value));
            Assert.True(headers.TryGetValue("Content-Length", out value));
        }

        [Fact]
        public void ClearRemovesAllHeaders()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();
            headers.Add("host", new[] { "localhost" });
            headers.Add("custom", new[] { "value" });
            headers.Add("Content-Length", new[] { "0" });

            StringValues value;
            Assert.Equal(3, headers.Count);
            Assert.True(headers.TryGetValue("host", out value));
            Assert.True(headers.TryGetValue("custom", out value));
            Assert.True(headers.TryGetValue("Content-Length", out value));

            headers.Clear();

            Assert.Equal(0, headers.Count);
            Assert.False(headers.TryGetValue("host", out value));
            Assert.False(headers.TryGetValue("custom", out value));
            Assert.False(headers.TryGetValue("Content-Length", out value));
        }

        [Fact]
        public void RemoveTakesHeadersOutOfDictionary()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();
            headers.Add("host", new[] { "localhost" });
            headers.Add("custom", new[] { "value" });
            headers.Add("Content-Length", new[] { "0" });

            StringValues value;
            Assert.Equal(3, headers.Count);
            Assert.True(headers.TryGetValue("host", out value));
            Assert.True(headers.TryGetValue("custom", out value));
            Assert.True(headers.TryGetValue("Content-Length", out value));

            Assert.True(headers.Remove("host"));
            Assert.False(headers.Remove("host"));

            Assert.Equal(2, headers.Count);
            Assert.False(headers.TryGetValue("host", out value));
            Assert.True(headers.TryGetValue("custom", out value));

            Assert.True(headers.Remove("custom"));
            Assert.False(headers.Remove("custom"));

            Assert.Equal(1, headers.Count);
            Assert.False(headers.TryGetValue("host", out value));
            Assert.False(headers.TryGetValue("custom", out value));
            Assert.True(headers.TryGetValue("Content-Length", out value));

            Assert.True(headers.Remove("Content-Length"));
            Assert.False(headers.Remove("Content-Length"));

            Assert.Equal(0, headers.Count);
            Assert.False(headers.TryGetValue("host", out value));
            Assert.False(headers.TryGetValue("custom", out value));
            Assert.False(headers.TryGetValue("Content-Length", out value));
        }

        [Fact]
        public void CopyToMovesDataIntoArray()
        {
            IDictionary<string, StringValues> headers = new FrameRequestHeaders();
            headers.Add("host", new[] { "localhost" });
            headers.Add("Content-Length", new[] { "0" });
            headers.Add("custom", new[] { "value" });

            var entries = new KeyValuePair<string, StringValues>[5];
            headers.CopyTo(entries, 1);

            Assert.Null(entries[0].Key);
            Assert.Equal(new StringValues(), entries[0].Value);

            Assert.Equal("Host", entries[1].Key);
            Assert.Equal(new[] { "localhost" }, entries[1].Value);

            Assert.Equal("Content-Length", entries[2].Key);
            Assert.Equal(new[] { "0" }, entries[2].Value);

            Assert.Equal("custom", entries[3].Key);
            Assert.Equal(new[] { "value" }, entries[3].Value);

            Assert.Null(entries[4].Key);
            Assert.Equal(new StringValues(), entries[4].Value);
        }

        [Theory]
        [MemberData(nameof(RequestsWithInvalidHeaders))]
        public void ParseThrowsWhenHeadersContainNonAsciiCharacters(byte[] data)
        {
            SetupParser();
            InsertData(data);

            var exception = Assert.Throws<BadHttpRequestException>(() => ParseData());
            Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        }

        public static TheoryData<byte[]> RequestsWithInvalidHeaders
        {
            get
            {
                var encoding = Encoding.GetEncoding("iso-8859-1");
                var start = "GET /plaintext HTTP/1.1\r\n" +
                                    "Host: localhost\r\n";
                var end = "\r\nAccept: text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7\r\n" +
                                    "Connection: keep-alive\r\n\r\n";

                return new TheoryData<byte[]>
                {
                    encoding.GetBytes(start + "Name: \u00141ód\017c" + end),
                    encoding.GetBytes(start + "\u00141ód\017c: Value" + end),
                    encoding.GetBytes(start + "\u00141ód\017c: \u00141ód\017c" + end),
                    encoding.GetBytes(start + "Name\u00141ód\017c: \u00141ód\017c" + end),
                    encoding.GetBytes(start + "\u00141ód\017c: Value\u00141ód\017c" + end),
                    encoding.GetBytes(start + "Name\u00141ód\017c: Value\u00141ód\017c" + end),
                    encoding.GetBytes(start + "Name: 6789012345\u00141ód\017c" + end),
                    encoding.GetBytes(start + "Name: 67890123456\u00141ód\017c" + end),
                    encoding.GetBytes(start + "Name: 67890123456789012345678901\u00141ód\017c" + end),
                    encoding.GetBytes(start + "Name: 678901234567890123456789012\u00141ód\017c" + end),
                };
            }
        }

        public void SetupParser()
        {
            var connectionContext = new MockConnection(new KestrelServerOptions());
            connectionContext.ListenerContext.ServiceContext.HttpParserFactory = 
                frame => (IHttpParser)Activator.CreateInstance(typeof(KestrelHttpParser), 
                         frame.ConnectionContext.ListenerContext.ServiceContext.Log);

            Frame = new Frame<object>(application: null, context: connectionContext);
            PipelineFactory = new PipeFactory();
            Pipe = PipelineFactory.Create();
        }

        private void InsertData(byte[] bytes)
        {
            // There should not be any backpressure and task completes immediately
            Pipe.Writer.WriteAsync(bytes).GetAwaiter().GetResult();
        }

        private void ParseData()
        {
            do
            {
                var awaitable = Pipe.Reader.ReadAsync();
                if (!awaitable.IsCompleted)
                {
                    // No more data
                    return;
                }

                var result = awaitable.GetAwaiter().GetResult();
                var readableBuffer = result.Buffer;

                Frame.Reset();

                if (!Frame.TakeStartLine(readableBuffer, out var consumed, out var examined))
                {
                    ThrowInvalidStartLine();
                }
                Pipe.Reader.Advance(consumed, examined);

                result = Pipe.Reader.ReadAsync().GetAwaiter().GetResult();
                readableBuffer = result.Buffer;

                Frame.InitializeHeaders();

                if (!Frame.TakeMessageHeaders(readableBuffer, out consumed, out examined))
                {
                    ThrowInvalidMessageHeaders();
                }
                Pipe.Reader.Advance(consumed, examined);
            }
            while (true);
        }

        public IPipe Pipe { get; set; }

        public Frame<object> Frame { get; set; }

        public PipeFactory PipelineFactory { get; set; }

        private void ThrowInvalidStartLine()
        {
            throw new InvalidOperationException("Invalid StartLine");
        }

        private void ThrowInvalidMessageHeaders()
        {
            throw new InvalidOperationException("Invalid MessageHeaders");
        }
    }
}
