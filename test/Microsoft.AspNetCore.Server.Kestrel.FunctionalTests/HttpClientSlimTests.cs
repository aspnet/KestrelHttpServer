// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class HttpClientSlimTests
    {
        [Fact]
        public async Task GetStringAsyncHttp()
        {
            using (var host = StartHost())
            {
                Assert.Equal("test", await HttpClientSlim.GetStringAsync(host.GetUri()));
            }
        }

        [Fact]
        public async Task GetStringAsyncHttps()
        {
            using (var host = StartHost(protocol: "https"))
            {
                Assert.Equal("test", await HttpClientSlim.GetStringAsync(host.GetUri(), validateCertificate: false));
            }
        }

        [Fact]
        public async Task GetStringAsyncThrowsForErrorResponse()
        {
            using (var host = StartHost(statusCode: 500))
            {
                await Assert.ThrowsAnyAsync<HttpRequestException>(() => HttpClientSlim.GetStringAsync(host.GetUri()));
            }
        }

        private IWebHost StartHost(string protocol = "http", int statusCode = 200)
        {
            var host = new WebHostBuilder()
                .UseUrls($"{protocol}://127.0.0.1:0")
                .UseKestrel(options =>
                {
                    options.UseHttps(@"TestResources/testCert.pfx", "testPassword");
                })
                .Configure((app) =>
                {
                    app.Run(context =>
                    {
                        context.Response.StatusCode = statusCode;
                        return context.Response.WriteAsync("test");
                    });
                })
                .Build();

            host.Start();
            return host;
        }
    }
}
