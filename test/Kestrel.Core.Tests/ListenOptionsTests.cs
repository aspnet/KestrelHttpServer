﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class ListenOptionsTests
    {
        [Fact]
        public void ProtocolsDefault()
        {
            var listenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, 0));
            Assert.Equal(HttpProtocols.Http1, listenOptions.Protocols);
        }

        [Fact]
        public void Http2DisabledByDefault()
        {
            var listenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, 0));
            var ex = Assert.Throws<NotSupportedException>(() => listenOptions.Protocols = HttpProtocols.Http1AndHttp2);
            Assert.Equal(CoreStrings.Http2NotSupported, ex.Message);
            ex = Assert.Throws<NotSupportedException>(() => listenOptions.Protocols = HttpProtocols.Http2);
            Assert.Equal(CoreStrings.Http2NotSupported, ex.Message);
        }

        [Fact]
        public void LocalHostListenOptionsClonesConnectionMiddleware()
        {
            var localhostListenOptions = new LocalhostListenOptions(1004);
            localhostListenOptions.ConnectionAdapters.Add(new PassThroughConnectionAdapter());
            var serviceProvider = new ServiceCollection().BuildServiceProvider();
            localhostListenOptions.KestrelServerOptions = new KestrelServerOptions()
            {
                ApplicationServices = serviceProvider
            };
            var middlewareRan = false;
            localhostListenOptions.Use(next =>
            {
                middlewareRan = true;
                return context => Task.CompletedTask;
            });

            var clone = localhostListenOptions.Clone(IPAddress.IPv6Loopback);
            var app = clone.Build();

            // Execute the delegate
            app(null);

            Assert.True(middlewareRan);
            Assert.NotNull(clone.KestrelServerOptions);
            Assert.NotNull(serviceProvider);
            Assert.Same(serviceProvider, clone.ApplicationServices);
            Assert.Single(clone.ConnectionAdapters);
        }
    }
}
