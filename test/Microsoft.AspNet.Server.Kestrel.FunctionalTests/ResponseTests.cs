// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.AspNet.Server.Kestrel.FunctionalTests
{
    public class ResponseTests
    {
        const int BufferSize = 1024;
        const int SendCount = 1024;
        const int TotalSize = BufferSize * SendCount;

        [Fact]
        public async Task LargeDownload()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "server.urls", "http://localhost:8792/" }
                })
                .Build();

            var hostBuilder = new WebHostBuilder(config);
            hostBuilder.UseServerFactory("Microsoft.AspNet.Server.Kestrel");
            hostBuilder.UseStartup(app =>
            {
                app.Run(async context =>
                {
                    var bytes = new byte[BufferSize];
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        bytes[i] = (byte)i;
                    }

                    context.Response.ContentLength = bytes.Length * SendCount;

                    for (int i = 0; i < SendCount; i++)
                    {
                        await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
                    }
                });
            });

            using (var app = hostBuilder.Build().Start())
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync("http://localhost:8792/");
                    response.EnsureSuccessStatusCode();
                    var responseBody = await response.Content.ReadAsStreamAsync();

                    // Read the full response body
                    var total = 0;
                    var bytes = new byte[BufferSize];
                    var count = await responseBody.ReadAsync(bytes, 0, bytes.Length);
                    while (count > 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            Assert.Equal(total % 256, bytes[i]);
                            total++;
                        }
                        count = await responseBody.ReadAsync(bytes, 0, bytes.Length);
                    }
                    Assert.Equal(total, TotalSize);
                }
            }
        }
    }
}
