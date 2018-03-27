// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.FunctionalTests;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests
{
    public class DefaultHeaderTests : LoggedTest
    {
        public DefaultHeaderTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task TestDefaultHeaders()
        {
            using (StartLog(out var loggerFactory, TestConstants.DefaultFunctionalTestLogLevel))
            {
                var testContext = new TestServiceContext()
                {
                    ServerOptions = { AddServerHeader = true },
                    LoggerFactory = loggerFactory
                };

                using (var server = new TestServer(ctx => Task.CompletedTask, testContext))
                {
                    using (var connection = server.CreateConnection())
                    {
                        await connection.Send(
                            "GET / HTTP/1.1",
                            "Host:",
                            "",
                            "GET / HTTP/1.0",
                            "",
                            "");

                        await connection.ReceiveForcedEnd(
                            "HTTP/1.1 200 OK",
                            $"Date: {testContext.DateHeaderValue}",
                            "Server: Kestrel",
                            "Content-Length: 0",
                            "",
                            "HTTP/1.1 200 OK",
                            "Connection: close",
                            $"Date: {testContext.DateHeaderValue}",
                            "Server: Kestrel",
                            "Content-Length: 0",
                            "",
                            "");
                    }
                }
            }
        }
    }
}
