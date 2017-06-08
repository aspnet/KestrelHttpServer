﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class DefaultHeaderTests
    {
        [Fact]
        public async Task TestDefaultHeaders()
        {
            var testContext = new TestServiceContext()
            {
                ServerOptions = { AddServerHeader = true }
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
