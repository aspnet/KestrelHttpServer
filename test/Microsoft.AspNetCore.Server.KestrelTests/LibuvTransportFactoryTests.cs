// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class LibuvTransportFactoryTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1337)]
        public void StartWithNonPositiveThreadCountThrows(int threadCount)
        {
            var options = new LibuvTransportOptions() { ThreadCount = threadCount };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LibuvTransportFactory(Options.Create(options), new LifetimeNotImplemented(), Mock.Of<ILoggerFactory>()));

            Assert.Equal("threadCount", exception.ParamName);
        }

        [Fact]
        public void LoggerCategoryNameIsKestrelServerNamespace()
        {
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            new LibuvTransportFactory(Options.Create<LibuvTransportOptions>(new LibuvTransportOptions()), new LifetimeNotImplemented(), mockLoggerFactory.Object);
            mockLoggerFactory.Verify(factory => factory.CreateLogger("Microsoft.AspNetCore.Server.Kestrel"));
        }
    }
}
