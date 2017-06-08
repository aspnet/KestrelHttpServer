// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class RequestHeaderProcessingTests
    {
        /// <summary>
        /// TFS-221201: Illegal trailing whitespace should cause subsequent headers with leading whitespace to be ignored
        ///
        /// Send headers with whitespace prepended and determine if they were mistakenly accepted by the server.
        /// </summary>
        [Theory]
        [InlineData("X-Valid: Hello", 200)]
        [InlineData(" X-Invalid: Foo", 400)]
        [InlineData("\tX-Invalid: Foo", 400)]
        public async Task LeadingWhitespaceIsRejected(string header, int statusCode)
        {
            using (var server = CreateServer())
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                      "GET / HTTP/1.1",
                      "Host:",
                      $"{header}\r\n\r\n");
                    await connection.Receive($"HTTP/1.1 {statusCode}");
                }
            }
        }

        private TestServer CreateServer()
        {
            return new TestServer(async httpContext => await httpContext.Response.WriteAsync("hello, world"), new TestServiceContext
            {
                SystemClock = new SystemClock(),
                ServerOptions =
                {
                    AddServerHeader = false
                }
            });
        }
    }
}
