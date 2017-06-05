// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class RequestBodyTimeoutTests
    {
        [Fact]
        public async Task RequestTimesOutIfRequestBodyNotReceivedWithinTimeoutPeriod()
        {
            var systemClock = new MockSystemClock();
            var serviceContext = new TestServiceContext
            {
                SystemClock = systemClock,
                DateHeaderValueManager = new DateHeaderValueManager(systemClock)
            };

            var appRunningEvent = new ManualResetEventSlim();

            using (var server = new TestServer(context =>
            {
                appRunningEvent.Set();
                return context.Request.Body.ReadAsync(new byte[1], 0, 1);
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "POST / HTTP/1.1",
                        "Host:",
                        "Content-Length: 1",
                        "",
                        "");

                    Assert.True(appRunningEvent.Wait(TimeSpan.FromSeconds(10)));
                    systemClock.UtcNow += serviceContext.ServerOptions.Limits.DefaultRequestBodyTimeout + TimeSpan.FromSeconds(1);

                    await connection.Receive(
                        "HTTP/1.1 408 Request Timeout",
                        "");
                    await connection.ReceiveForcedEnd(
                        "Connection: close",
                        $"Date: {serviceContext.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task RequestTimesOutEvenIfNotConsumedByApp()
        {
            var systemClock = new MockSystemClock();
            var serviceContext = new TestServiceContext
            {
                SystemClock = systemClock,
                DateHeaderValueManager = new DateHeaderValueManager(systemClock)
            };

            var appRunningEvent = new ManualResetEventSlim();

            using (var server = new TestServer(context =>
            {
                appRunningEvent.Set();
                return Task.CompletedTask;
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "POST / HTTP/1.1",
                        "Host:",
                        "Content-Length: 1",
                        "",
                        "");

                    Assert.True(appRunningEvent.Wait(TimeSpan.FromSeconds(10)));
                    systemClock.UtcNow += serviceContext.ServerOptions.Limits.DefaultRequestBodyTimeout + TimeSpan.FromSeconds(1);

                    await connection.Receive(
                        "HTTP/1.1 408 Request Timeout",
                        "");
                    await connection.ReceiveForcedEnd(
                        "Connection: close",
                        $"Date: {serviceContext.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task ConnectionClosedEvenIfAppSwallowsException()
        {
            var systemClock = new MockSystemClock();
            var serviceContext = new TestServiceContext
            {
                SystemClock = systemClock,
                DateHeaderValueManager = new DateHeaderValueManager(systemClock)
            };

            var appRunningEvent = new ManualResetEventSlim();
            var exceptionSwallowedEvent = new ManualResetEventSlim();

            using (var server = new TestServer(async context =>
            {
                appRunningEvent.Set();

                try
                {
                    await context.Request.Body.ReadAsync(new byte[1], 0, 1);
                }
                catch (BadHttpRequestException ex) when (ex.StatusCode == 408)
                {
                    exceptionSwallowedEvent.Set();
                }

                var response = "hello, world";
                context.Response.ContentLength = response.Length;
                await context.Response.WriteAsync("hello, world");
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "POST / HTTP/1.1",
                        "Host:",
                        "Content-Length: 1",
                        "",
                        "");

                    Assert.True(appRunningEvent.Wait(TimeSpan.FromSeconds(10)));
                    systemClock.UtcNow += serviceContext.ServerOptions.Limits.DefaultRequestBodyTimeout + TimeSpan.FromSeconds(1);
                    Assert.True(exceptionSwallowedEvent.Wait(TimeSpan.FromSeconds(10)));

                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        "");
                    await connection.ReceiveForcedEnd(
                        $"Date: {serviceContext.DateHeaderValue}",
                        "Content-Length: 12",
                        "",
                        "hello, world");
                }
            }
        }
    }
}
