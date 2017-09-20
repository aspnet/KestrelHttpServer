// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class Http2Tests
    {
        [Fact]
        public async Task ServesHttp2WithPriorKnowledge()
        {
            var builder = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
                })
                .Configure(app => app.Run(context => Task.CompletedTask));

            using (var host = builder.Build())
            {
                host.Start();

                using (var connection = new TestConnection(host.GetPort()))
                {
                    await connection.Send(Encoding.ASCII.GetString(Http2Connection.ClientPreface));

                    // Expect a SETTINGS frame (type 0x4) with no payload and no flags
                    await connection.Receive("\x00\x00\x00\x04\x00\x00\x00\x00\x00");
                }
            }
        }
    }
}
