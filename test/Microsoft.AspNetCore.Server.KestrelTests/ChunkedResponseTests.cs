// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class ChunkedResponseTests
    {
        [Fact]
        public async Task ResponsesAreChunkedAutomatically()
        {
            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                response.Headers.Clear();
                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("Hello "), 0, 6);
                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("World!"), 0, 6);
            }))
            {
                using (var connection = new TestConnection(server.Port))
                {
                    await connection.SendEnd(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        "Transfer-Encoding: chunked",
                        "",
                        "6",
                        "Hello ",
                        "6",
                        "World!",
                        "0",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task ZeroLengthWritesAreIgnored()
        {
            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                response.Headers.Clear();
                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("Hello "), 0, 6);
                await response.Body.WriteAsync(new byte[0], 0, 0);
                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("World!"), 0, 6);
            }))
            {
                using (var connection = new TestConnection(server.Port))
                {
                    await connection.SendEnd(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        "Transfer-Encoding: chunked",
                        "",
                        "6",
                        "Hello ",
                        "6",
                        "World!",
                        "0",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task EmptyResponseBodyHandledCorrectlyWithZeroLengthWrite()
        {
            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                response.Headers.Clear();
                await response.Body.WriteAsync(new byte[0], 0, 0);
            }))
            {
                using (var connection = new TestConnection(server.Port))
                {
                    await connection.SendEnd(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        "Transfer-Encoding: chunked",
                        "",
                        "0",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task ConnectionClosedIfExeptionThrownAfterWrite()
        {
            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                response.Headers.Clear();
                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("Hello World!"), 0, 12);
                throw new Exception();
            }))
            {
                using (var connection = new TestConnection(server.Port))
                {
                    // SendEnd is not called, so it isn't the client closing the connection.
                    // client closing the connection.
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        "Transfer-Encoding: chunked",
                        "",
                        "c",
                        "Hello World!",
                        "");
                }
            }
        }

        [Fact]
        public async Task ConnectionClosedIfExeptionThrownAfterZeroLengthWrite()
        {
            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                response.Headers.Clear();
                await response.Body.WriteAsync(new byte[0], 0, 0);
                throw new Exception();
            }))
            {
                using (var connection = new TestConnection(server.Port))
                {
                    // SendEnd is not called, so it isn't the client closing the connection.
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");

                    // Headers are sent before connection is closed, but chunked body terminator isn't sent
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        "Transfer-Encoding: chunked",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task WritesAreFlushedPriorToResponseCompletion()
        {
            var flushWh = new ManualResetEventSlim();

            using (var server = new TestServer(async httpContext =>
            {
                var response = httpContext.Response;
                response.Headers.Clear();
                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("Hello "), 0, 6);

                // Don't complete response until client has received the first chunk.
                flushWh.Wait();

                await response.Body.WriteAsync(Encoding.ASCII.GetBytes("World!"), 0, 6);
            }))
            {
                using (var connection = new TestConnection(server.Port))
                {
                    await connection.SendEnd(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        "Transfer-Encoding: chunked",
                        "",
                        "6",
                        "Hello ",
                        "");

                    flushWh.Set();

                    await connection.ReceiveEnd(
                        "6",
                        "World!",
                        "0",
                        "",
                        "");
                }
            }
        }
    }
}

