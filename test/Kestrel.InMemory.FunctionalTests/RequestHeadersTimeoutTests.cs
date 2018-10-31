// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.InMemory.FunctionalTests.TestTransport;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.InMemory.FunctionalTests
{
    public class RequestHeadersTimeoutTests : LoggedTest
    {
        private static readonly TimeSpan RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan LongDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ShortDelay = TimeSpan.FromSeconds(LongDelay.TotalSeconds / 10);

        [Theory]
        [InlineData("Host:\r\n")]
        [InlineData("Host:\r\nContent-Length: 1\r\n")]
        [InlineData("Host:\r\nContent-Length: 1\r\n\r")]
        public async Task ConnectionAbortedWhenRequestHeadersNotReceivedInTime(string headers)
        {
            var testContext = new TestServiceContext(LoggerFactory);
            var heartbeatManager = new HeartbeatManager(testContext.ConnectionManager);

            using (var server = CreateServer(testContext))
            using (var connection = server.CreateConnection())
            {
                await connection.Send(
                    "GET / HTTP/1.1",
                    headers);

                // Min amount of time between requests that triggers a request headers timeout.
                testContext.MockSystemClock.UtcNow += RequestHeadersTimeout + Heartbeat.Interval + TimeSpan.FromTicks(1);
                heartbeatManager.OnHeartbeat(testContext.SystemClock.UtcNow);

                await ReceiveTimeoutResponse(connection, testContext);
            }
        }

        [Fact]
        public async Task RequestHeadersTimeoutCanceledAfterHeadersReceived()
        {
            var testContext = new TestServiceContext(LoggerFactory);
            var heartbeatManager = new HeartbeatManager(testContext.ConnectionManager);

            using (var server = CreateServer(testContext))
            using (var connection = server.CreateConnection())
            {
                await connection.Send(
                    "POST / HTTP/1.1",
                    "Host:",
                    "Content-Length: 1",
                    "",
                    "");

                // Min amount of time between requests that triggers a request headers timeout.
                testContext.MockSystemClock.UtcNow += RequestHeadersTimeout + Heartbeat.Interval + TimeSpan.FromTicks(1);
                heartbeatManager.OnHeartbeat(testContext.SystemClock.UtcNow);

                await connection.Send(
                    "a");

                await ReceiveResponse(connection, testContext);
            }
        }

        [Theory]
        [InlineData("P")]
        [InlineData("POST / HTTP/1.1\r")]
        public async Task ConnectionAbortedWhenRequestLineNotReceivedInTime(string requestLine)
        {
            var testContext = new TestServiceContext(LoggerFactory);
            var heartbeatManager = new HeartbeatManager(testContext.ConnectionManager);

            using (var server = CreateServer(testContext))
            using (var connection = server.CreateConnection())
            {
                await connection.Send(requestLine);

                // Min amount of time between requests that triggers a request headers timeout.
                testContext.MockSystemClock.UtcNow += RequestHeadersTimeout + Heartbeat.Interval + TimeSpan.FromTicks(1);
                heartbeatManager.OnHeartbeat(testContext.SystemClock.UtcNow);

                await ReceiveTimeoutResponse(connection, testContext);
            }
        }

        [Fact]
        public async Task TimeoutNotResetOnEachRequestLineCharacterReceived()
        {
            var testContext = new TestServiceContext(LoggerFactory);
            var heartbeatManager = new HeartbeatManager(testContext.ConnectionManager);

            // Disable response rate, so we can finish the send loop without timing out the response.
            testContext.ServerOptions.Limits.MinResponseDataRate = null;

            using (var server = CreateServer(testContext))
            using (var connection = server.CreateConnection())
            {
                foreach (var ch in "POST / HTTP/1.1\r\nHost:\r\n\r\n")
                {
                    await connection.Send(ch.ToString());

                    testContext.MockSystemClock.UtcNow += ShortDelay;
                    heartbeatManager.OnHeartbeat(testContext.SystemClock.UtcNow);
                }

                await ReceiveTimeoutResponse(connection, testContext);

                await connection.WaitForConnectionClose();
            }
        }

        private TestServer CreateServer(TestServiceContext context)
        {
            // Ensure request headers timeout is started as soon as the tests send requests.
            context.Scheduler = PipeScheduler.Inline;
            context.ServerOptions.Limits.RequestHeadersTimeout = RequestHeadersTimeout;
            context.ServerOptions.Limits.MinRequestBodyDataRate = null;

            return new TestServer(async httpContext =>
            {
                await httpContext.Request.Body.ReadAsync(new byte[1], 0, 1);
                await httpContext.Response.WriteAsync("hello, world");
            }, context);
        }

        private async Task ReceiveResponse(InMemoryConnection connection, TestServiceContext testContext)
        {
            await connection.Receive(
                "HTTP/1.1 200 OK",
                $"Date: {testContext.DateHeaderValue}",
                "Transfer-Encoding: chunked",
                "",
                "c",
                "hello, world",
                "0",
                "",
                "");
        }

        private async Task ReceiveTimeoutResponse(InMemoryConnection connection, TestServiceContext testContext)
        {
            await connection.Receive(
                "HTTP/1.1 408 Request Timeout",
                "Connection: close",
                $"Date: {testContext.DateHeaderValue}",
                "Content-Length: 0",
                "",
                "");
        }
    }
}