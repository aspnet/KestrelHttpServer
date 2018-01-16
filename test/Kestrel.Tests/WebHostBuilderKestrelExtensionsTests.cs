// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Tests
{
    public class WebHostBuilderKestrelExtensionsTests
    {
        [Fact]
        public void ApplicationServicesNotNullAfterUseKestrelWithoutOptions()
        {
            // Arrange
            var hostBuilder = new WebHostBuilder()
                .UseKestrel()
                .Configure(app => { });

            hostBuilder.ConfigureServices(services =>
            {
                services.Configure<KestrelServerOptions>(options =>
                {
                    // Assert
                    Assert.NotNull(options.ApplicationServices);
                });
            });

            // Act
            hostBuilder.Build();
        }

        [Fact]
        public void ApplicationServicesNotNullDuringUseKestrelWithOptions()
        {
            // Arrange
            var hostBuilder = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    // Assert
                    Assert.NotNull(options.ApplicationServices);
                })
                .Configure(app => { });

            // Act
            hostBuilder.Build();
        }

        [Fact]
        public void LibuvIsTheDefaultTransport()
        {
            var hostBuilder = new WebHostBuilder()
                .UseKestrel()
                .Configure(app => { });

            Assert.IsType<LibuvTransportFactory>(hostBuilder.Build().Services.GetService<ITransportFactory>());
        }

        [Fact]
        public void LibuvTransportCanBeManuallySelected()
        {
            var hostBuilder = new WebHostBuilder()
                .UseKestrel()
                .UseLibuv()
                .Configure(app => { });

            Assert.IsType<LibuvTransportFactory>(hostBuilder.Build().Services.GetService<ITransportFactory>());
        }


        [Fact]
        public void SocketsTransportCanBeManuallySelected()
        {
            var hostBuilder = new WebHostBuilder()
                .UseKestrel()
                .UseSockets()
                .Configure(app => { });

            Assert.IsType<SocketTransportFactory>(hostBuilder.Build().Services.GetService<ITransportFactory>());
        }
    }
}
