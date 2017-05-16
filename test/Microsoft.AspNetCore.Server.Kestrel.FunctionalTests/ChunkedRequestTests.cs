// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Internal;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class ChunkedRequestTests
    {
        public static TheoryData<ListenOptions> ConnectionAdapterData => new TheoryData<ListenOptions>
        {
            new ListenOptions(new IPEndPoint(IPAddress.Loopback, 0)),
            new ListenOptions(new IPEndPoint(IPAddress.Loopback, 0))
            {
                ConnectionAdapters = { new PassThroughConnectionAdapter() }
            }
        };

        private async Task App(HttpContext httpContext)
        {
            var request = httpContext.Request;
            var response = httpContext.Response;
            while (true)
            {
                var buffer = new byte[8192];
                var count = await request.Body.ReadAsync(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    break;
                }
                await response.Body.WriteAsync(buffer, 0, count);
            }
        }

        private async Task AppChunked(HttpContext httpContext)
        {
            var request = httpContext.Request;
            var response = httpContext.Response;
            var data = new MemoryStream();
            await request.Body.CopyToAsync(data);
            var bytes = data.ToArray();

            response.Headers["Content-Length"] = bytes.Length.ToString();
            await response.Body.WriteAsync(bytes, 0, bytes.Length);
        }

        [Theory]
        [MemberData(nameof(ConnectionAdapterData))]
        public async Task Http10TransferEncoding(ListenOptions listenOptions)
        {
            var testContext = new TestServiceContext();

            using (var server = new TestServer(App, testContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "POST / HTTP/1.0",
                        "Host:",
                        "Transfer-Encoding: chunked",
                        "",
                        "5", "Hello",
                        "6", " World",
                        "0",
                         "",
                         "");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        "Connection: close",
                        $"Date: {testContext.DateHeaderValue}",
                        "",
                        "Hello World");
                }
            }
        }

        [Theory]
        [MemberData(nameof(ConnectionAdapterData))]
        public async Task Http10KeepAliveTransferEncoding(ListenOptions listenOptions)
        {
            var testContext = new TestServiceContext();

            using (var server = new TestServer(AppChunked, testContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "POST / HTTP/1.0",
                        "Host:",
                        "Transfer-Encoding: chunked",
                        "Connection: keep-alive",
                        "",
                        "5", "Hello",
                        "6", " World",
                        "0",
                         "",
                        "POST / HTTP/1.0",
                        "Content-Length: 7",
                        "",
                        "Goodbye");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        "Connection: keep-alive",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 11",
                        "",
                        "Hello World");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        "Connection: close",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 7",
                        "",
                        "Goodbye");
                }
            }
        }

        [Theory]
        [MemberData(nameof(ConnectionAdapterData))]
        public async Task RequestBodyIsConsumedAutomaticallyIfAppDoesntConsumeItFully(ListenOptions listenOptions)
        {
            var testContext = new TestServiceContext();

            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                var request = httpContext.Request;

                Assert.Equal("POST", request.Method);

                response.Headers["Content-Length"] = new[] { "11" };

                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("Hello World"), 0, 11);
            }, testContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "POST / HTTP/1.1",
                        "Host:",
                        "Content-Length: 5",
                        "",
                        "HelloPOST / HTTP/1.1",
                        "Host:",
                        "Transfer-Encoding: chunked",
                        "",
                        "C", "HelloChunked",
                        "0",
                        "",
                        "POST / HTTP/1.1",
                        "Host:",
                        "Content-Length: 7",
                        "",
                        "Goodbye");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 11",
                        "",
                        "Hello WorldHTTP/1.1 200 OK",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 11",
                        "",
                        "Hello WorldHTTP/1.1 200 OK",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 11",
                        "",
                        "Hello World");
                }
            }
        }

        [Theory]
        [MemberData(nameof(ConnectionAdapterData))]
        public async Task TrailingHeadersAreParsed(ListenOptions listenOptions)
        {
            var requestCount = 10;
            var requestsReceived = 0;

            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                var request = httpContext.Request;

                var buffer = new byte[200];

                while (await request.Body.ReadAsync(buffer, 0, buffer.Length) != 0)
                {
                    ;// read to end
                }

                if (requestsReceived < requestCount)
                {
                    Assert.Equal(new string('a', requestsReceived), request.Headers["X-Trailer-Header"].ToString());
                }
                else
                {
                    Assert.True(string.IsNullOrEmpty(request.Headers["X-Trailer-Header"]));
                }

                requestsReceived++;

                response.Headers["Content-Length"] = new[] { "11" };

                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("Hello World"), 0, 11);
            }, new TestServiceContext(), listenOptions))
            {
                var response = string.Join("\r\n", new string[] {
                    "HTTP/1.1 200 OK",
                    $"Date: {server.Context.DateHeaderValue}",
                    "Content-Length: 11",
                    "",
                    "Hello World"});

                var expectedFullResponse = string.Join("", Enumerable.Repeat(response, requestCount + 1));

                IEnumerable<string> sendSequence = new string[] {
                    "POST / HTTP/1.1",
                    "Host:",
                    "Transfer-Encoding: chunked",
                    "",
                    "C",
                    "HelloChunked",
                    "0",
                    ""};

                for (var i = 1; i < requestCount; i++)
                {
                    sendSequence = sendSequence.Concat(new string[] {
                        "POST / HTTP/1.1",
                        "Host:",
                        "Transfer-Encoding: chunked",
                        "",
                        "C",
                        $"HelloChunk{i:00}",
                        "0",
                        string.Concat("X-Trailer-Header: ", new string('a', i)),
                        "" });
                }

                sendSequence = sendSequence.Concat(new string[] {
                    "POST / HTTP/1.1",
                    "Host:",
                    "Content-Length: 7",
                    "",
                    "Goodbye"
                });

                var fullRequest = sendSequence.ToArray();

                using (var connection = server.CreateConnection())
                {
                    await connection.Send(fullRequest);
                    await connection.ReceiveEnd(expectedFullResponse);
                }
            }
        }

        [Theory]
        [MemberData(nameof(ConnectionAdapterData))]
        public async Task TrailingHeadersCountTowardsHeadersTotalSizeLimit(ListenOptions listenOptions)
        {
            const string transferEncodingHeaderLine = "Transfer-Encoding: chunked";
            const string headerLine = "Header: value";
            const string trailingHeaderLine = "Trailing-Header: trailing-value";

            var testContext = new TestServiceContext();
            testContext.ServerOptions.Limits.MaxRequestHeadersTotalSize =
                transferEncodingHeaderLine.Length + 2 +
                headerLine.Length + 2 +
                trailingHeaderLine.Length + 1;

            using (var server = new TestServer(async context =>
            {
                var buffer = new byte[128];
                while (await context.Request.Body.ReadAsync(buffer, 0, buffer.Length) != 0) ; // read to end
            }, testContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll(
                        "POST / HTTP/1.1",
                        "Host:",
                        $"{transferEncodingHeaderLine}",
                        $"{headerLine}",
                        "",
                        "2",
                        "42",
                        "0",
                        $"{trailingHeaderLine}",
                        "",
                        "");
                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 431 Request Header Fields Too Large",
                        "Connection: close",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }
        }

        [Theory]
        [MemberData(nameof(ConnectionAdapterData))]
        public async Task TrailingHeadersCountTowardsHeaderCountLimit(ListenOptions listenOptions)
        {
            const string transferEncodingHeaderLine = "Transfer-Encoding: chunked";
            const string headerLine = "Header: value";
            const string trailingHeaderLine = "Trailing-Header: trailing-value";

            var testContext = new TestServiceContext();
            testContext.ServerOptions.Limits.MaxRequestHeaderCount = 2;

            using (var server = new TestServer(async context =>
            {
                var buffer = new byte[128];
                while (await context.Request.Body.ReadAsync(buffer, 0, buffer.Length) != 0) ; // read to end
            }, testContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll(
                        "POST / HTTP/1.1",
                        "Host:",
                        $"{transferEncodingHeaderLine}",
                        $"{headerLine}",
                        "",
                        "2",
                        "42",
                        "0",
                        $"{trailingHeaderLine}",
                        "",
                        "");
                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 431 Request Header Fields Too Large",
                        "Connection: close",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }
        }

        [Theory]
        [MemberData(nameof(ConnectionAdapterData))]
        public async Task ExtensionsAreIgnored(ListenOptions listenOptions)
        {
            var testContext = new TestServiceContext();
            var requestCount = 10;
            var requestsReceived = 0;

            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                var request = httpContext.Request;

                var buffer = new byte[200];

                while (await request.Body.ReadAsync(buffer, 0, buffer.Length) != 0)
                {
                    ;// read to end
                }

                if (requestsReceived < requestCount)
                {
                    Assert.Equal(new string('a', requestsReceived), request.Headers["X-Trailer-Header"].ToString());
                }
                else
                {
                    Assert.True(string.IsNullOrEmpty(request.Headers["X-Trailer-Header"]));
                }

                requestsReceived++;

                response.Headers["Content-Length"] = new[] { "11" };

                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("Hello World"), 0, 11);
            }, testContext, listenOptions))
            {
                var response = string.Join("\r\n", new string[] {
                    "HTTP/1.1 200 OK",
                    $"Date: {testContext.DateHeaderValue}",
                    "Content-Length: 11",
                    "",
                    "Hello World"});

                var expectedFullResponse = string.Join("", Enumerable.Repeat(response, requestCount + 1));

                IEnumerable<string> sendSequence = new string[] {
                    "POST / HTTP/1.1",
                    "Host:",
                    "Transfer-Encoding: chunked",
                    "",
                    "C;hello there",
                    "HelloChunked",
                    "0;hello there",
                    ""};

                for (var i = 1; i < requestCount; i++)
                {
                    sendSequence = sendSequence.Concat(new string[] {
                        "POST / HTTP/1.1",
                        "Host:",
                        "Transfer-Encoding: chunked",
                        "",
                        "C;hello there",
                        $"HelloChunk{i:00}",
                        "0;hello there",
                        string.Concat("X-Trailer-Header: ", new string('a', i)),
                        "" });
                }

                sendSequence = sendSequence.Concat(new string[] {
                    "POST / HTTP/1.1",
                    "Host:",
                    "Content-Length: 7",
                    "",
                    "Goodbye"
                });

                var fullRequest = sendSequence.ToArray();

                using (var connection = server.CreateConnection())
                {
                    await connection.Send(fullRequest);
                    await connection.ReceiveEnd(expectedFullResponse);
                }
            }
        }

        [Theory]
        [MemberData(nameof(ConnectionAdapterData))]
        public async Task InvalidLengthResultsIn400(ListenOptions listenOptions)
        {
            var testContext = new TestServiceContext();
            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                var request = httpContext.Request;

                var buffer = new byte[200];

                while (await request.Body.ReadAsync(buffer, 0, buffer.Length) != 0)
                {
                    ;// read to end
                }

                response.Headers["Content-Length"] = new[] { "11" };

                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("Hello World"), 0, 11);
            }, testContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll(
                        "POST / HTTP/1.1",
                        "Host:",
                        "Transfer-Encoding: chunked",
                        "",
                        "Cii");

                    await connection.Receive(
                        "HTTP/1.1 400 Bad Request",
                        "Connection: close",
                        "");
                    await connection.ReceiveForcedEnd(
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }
        }

        [Theory]
        [MemberData(nameof(ConnectionAdapterData))]
        public async Task InvalidSizedDataResultsIn400(ListenOptions listenOptions)
        {
            var testContext = new TestServiceContext();
            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                var request = httpContext.Request;

                var buffer = new byte[200];

                while (await request.Body.ReadAsync(buffer, 0, buffer.Length) != 0)
                {
                    ;// read to end
                }

                response.Headers["Content-Length"] = new[] { "11" };

                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("Hello World"), 0, 11);
            }, testContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll(
                        "POST / HTTP/1.1",
                        "Host:",
                        "Transfer-Encoding: chunked",
                        "",
                        "C",
                        "HelloChunkedIn");

                    await connection.Receive(
                        "HTTP/1.1 400 Bad Request",
                        "Connection: close",
                        "");
                    await connection.ReceiveForcedEnd(
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }
        }


        [Theory]
        [MemberData(nameof(ConnectionAdapterData))]
        public async Task ChunkedNotFinalTransferCodingResultsIn400(ListenOptions listenOptions)
        {
            var testContext = new TestServiceContext();
            using (var server = new TestServer(httpContext =>
            {
                return TaskCache.CompletedTask;
            }, testContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll(
                        "POST / HTTP/1.1",
                        "Host:",
                        "Transfer-Encoding: not-chunked",
                        "",
                        "C",
                        "hello, world",
                        "0",
                        "",
                        "");

                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 400 Bad Request",
                        "Connection: close",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }

                // Content-Length should not affect this
                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll(
                        "POST / HTTP/1.1",
                        "Host:",
                        "Transfer-Encoding: not-chunked",
                        "Content-Length: 22",
                        "",
                        "C",
                        "hello, world",
                        "0",
                        "",
                        "");

                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 400 Bad Request",
                        "Connection: close",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }

                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll(
                        "POST / HTTP/1.1",
                        "Host:",
                        "Transfer-Encoding: chunked, not-chunked",
                        "",
                        "C",
                        "hello, world",
                        "0",
                        "",
                        "");

                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 400 Bad Request",
                        "Connection: close",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }

                // Content-Length should not affect this
                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll(
                        "POST / HTTP/1.1",
                        "Host:",
                        "Transfer-Encoding: chunked, not-chunked",
                        "Content-Length: 22",
                        "",
                        "C",
                        "hello, world",
                        "0",
                        "",
                        "");

                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 400 Bad Request",
                        "Connection: close",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }
        }
    }
}

