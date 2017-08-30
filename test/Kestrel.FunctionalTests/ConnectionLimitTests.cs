﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Tests;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class ConnectionLimitTests
    {
        [Fact]
        public async Task ResetsCountWhenConnectionClosed()
        {
            var requestTcs = new TaskCompletionSource<object>();
            var releasedTcs = new TaskCompletionSource<object>();
            var lockedTcs = new TaskCompletionSource<bool>();
            var counter = new EventRaisingResourceCounter(ResourceCounter.Quota(1));
            counter.OnLock += (s, e) => lockedTcs.TrySetResult(e);
            counter.OnRelease += (s, e) => releasedTcs.TrySetResult(null);

            using (var server = CreateServerWithMaxConnections(async context =>
            {
                await context.Response.WriteAsync("Hello");
                await requestTcs.Task;
            }, counter))
            using (var connection = server.CreateConnection())
            {
                await connection.SendEmptyGetAsKeepAlive(); ;
                await connection.Receive("HTTP/1.1 200 OK");
                Assert.True(await lockedTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(10)));
                requestTcs.TrySetResult(null);
            }

            await releasedTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task UpgradedConnectionsCountsAgainstDifferentLimit()
        {
            using (var server = CreateServerWithMaxConnections(async context =>
            {
                var feature = context.Features.Get<IHttpUpgradeFeature>();
                if (feature.IsUpgradableRequest)
                {
                    var stream = await feature.UpgradeAsync();
                    // keep it running until aborted
                    while (!context.RequestAborted.IsCancellationRequested)
                    {
                        await Task.Delay(100);
                    }
                }
            }, max: 1))
            using (var disposables = new DisposableStack<TestConnection>())
            {
                var upgraded = server.CreateConnection();
                disposables.Push(upgraded);

                await upgraded.SendEmptyGetWithUpgrade();
                await upgraded.Receive("HTTP/1.1 101");
                // once upgraded, normal connection limit is decreased to allow room for more "normal" connections

                var connection = server.CreateConnection();
                disposables.Push(connection);

                await connection.SendEmptyGetAsKeepAlive();
                await connection.Receive("HTTP/1.1 200 OK");

                using (var rejected = server.CreateConnection())
                {
                    try
                    {
                        // this may throw IOException, depending on how fast Kestrel closes the socket
                        await rejected.SendEmptyGetAsKeepAlive();
                    }
                    catch { }

                    // connection should close without sending any data
                    await rejected.WaitForConnectionClose().TimeoutAfter(TimeSpan.FromSeconds(15));
                }
            }
        }

        [Fact]
        public async Task RejectsConnectionsWhenLimitReached()
        {
            const int max = 10;
            var requestTcs = new TaskCompletionSource<object>();

            using (var server = CreateServerWithMaxConnections(async context =>
            {
                await context.Response.WriteAsync("Hello");
                await requestTcs.Task;
            }, max))
            using (var disposables = new DisposableStack<TestConnection>())
            {
                for (var i = 0; i < max; i++)
                {
                    var connection = server.CreateConnection();
                    disposables.Push(connection);

                    await connection.SendEmptyGetAsKeepAlive();
                    await connection.Receive("HTTP/1.1 200 OK");
                }

                // limit has been reached
                for (var i = 0; i < 10; i++)
                {
                    using (var connection = server.CreateConnection())
                    {
                        try
                        {
                            // this may throw IOException, depending on how fast Kestrel closes the socket
                            await connection.SendEmptyGetAsKeepAlive();
                        } catch { }

                        // connection should close without sending any data
                        await connection.WaitForConnectionClose().TimeoutAfter(TimeSpan.FromSeconds(15));
                    }
                }

                requestTcs.TrySetResult(null);
            }
        }

        [Fact]
        public async Task ConnectionCountingReturnsToZero()
        {
            const int count = 100;
            var opened = 0;
            var closed = 0;
            var openedTcs = new TaskCompletionSource<object>();
            var closedTcs = new TaskCompletionSource<object>();

            var counter = new EventRaisingResourceCounter(ResourceCounter.Quota(uint.MaxValue));

            counter.OnLock += (o, e) =>
            {
                if (e && Interlocked.Increment(ref opened) >= count)
                {
                    openedTcs.TrySetResult(null);
                }
            };

            counter.OnRelease += (o, e) =>
            {
                if (Interlocked.Increment(ref closed) >= count)
                {
                    closedTcs.TrySetResult(null);
                }
            };

            using (var server = CreateServerWithMaxConnections(_ => Task.CompletedTask, counter))
            {
                // open a bunch of connections in parallel
                Parallel.For(0, count, async i =>
                {
                    try
                    {
                        using (var connection = server.CreateConnection())
                        {
                            await connection.SendEmptyGetAsKeepAlive();
                            await connection.Receive("HTTP/1.1 200");
                        }
                    }
                    catch (Exception ex)
                    {
                        closedTcs.TrySetException(ex);
                    }
                });

                // wait until resource counter has called lock for each connection
                await openedTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(120));
                // wait until resource counter has released all normal connections
                await closedTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(120));
                Assert.Equal(count, opened);
                Assert.Equal(count, closed);
            }
        }

        private TestServer CreateServerWithMaxConnections(RequestDelegate app, long max)
        {
            var serviceContext = new TestServiceContext();
            serviceContext.ServerOptions.Limits.MaxConcurrentConnections = max;
            return new TestServer(app, serviceContext);
        }

        private TestServer CreateServerWithMaxConnections(RequestDelegate app, ResourceCounter concurrentConnectionCounter)
        {
            var serviceContext = new TestServiceContext();
            var listenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, 0));
            listenOptions.Use(next =>
            {
                var middleware = new ConnectionLimitMiddleware(next, concurrentConnectionCounter, serviceContext.Log);
                return middleware.OnConnectionAsync;
            });

            return new TestServer(app, serviceContext, listenOptions);
        }
    }
}
