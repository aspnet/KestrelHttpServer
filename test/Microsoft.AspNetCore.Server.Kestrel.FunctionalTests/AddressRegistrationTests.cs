// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class AddressRegistrationTests
    {

        [Theory, MemberData(nameof(AddressRegistrationDataIPv4))]
        public async Task RegisterAddresses_IPv4_Success(string addressInput, Func<IServerAddressesFeature, string[]> testUrls)
        {
            await RegisterAddresses_Success(addressInput, testUrls);
        }

        [ConditionalTheory, MemberData(nameof(AddressRegistrationDataIPv6))]
        [IPv6SupportedCondition]
        public async Task RegisterAddresses_IPv6_Success(string addressInput, Func<IServerAddressesFeature, string[]> testUrls)
        {
            await RegisterAddresses_Success(addressInput, testUrls);
        }

        public async Task RegisterAddresses_Success(string addressInput, Func<IServerAddressesFeature, string[]> testUrls)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "server.urls", addressInput }
                })
                .Build();

            var hostBuilder = new WebHostBuilder()
                .UseConfiguration(config)
                .UseKestrel(options =>
                {
                    options.UseHttps(@"TestResources/testCert.pfx", "testPassword");
                })
                .Configure(ConfigureEchoAddress);

            using (var host = hostBuilder.Build())
            {
                host.Start();

                using (var client = new HttpClient())
                {
                    foreach (var testUrl in testUrls(host.ServerFeatures.Get<IServerAddressesFeature>()))
                    {
                        var responseText = await client.GetStringAsync(testUrl);
                        Assert.Equal(testUrl, responseText);
                    }
                }
            }
        }

        public static TheoryData<string, Func<IServerAddressesFeature, string[]>> AddressRegistrationDataIPv4
        {
            get
            {
                var dataset = new TheoryData<string, Func<IServerAddressesFeature, string[]>>();

                // Default host and port
                dataset.Add(null, _ => new[] { "http://localhost:5000/" });
                dataset.Add(string.Empty, _ => new[] { "http://localhost:5000/" });

                // Default port
                dataset.Add("http://*", _ => new[] { "http://localhost/" });
                dataset.Add("http://localhost", _ => new[] { "http://localhost/" });

                // Static port
                var port1 = GetNextPort();
                var port2 = GetNextPort();
                dataset.Add($"{port1}", _ => new[] { $"http://localhost:{port1}/" });
                dataset.Add($"{port1};{port2}", _ => new[] { $"http://localhost:{port1}/", $"http://localhost:{port2}/" });

                // Ensure "localhost" and "127.0.0.1" are equivalent
                dataset.Add($"http://localhost:{port1}", _ => new[] { $"http://localhost:{port1}/", $"http://127.0.0.1:{port1}/" });
                dataset.Add($"http://127.0.0.1:{port1}", _ => new[] { $"http://localhost:{port1}/", $"http://127.0.0.1:{port1}/" });

                // Path after port
                dataset.Add($"http://localhost:{port1}/base/path", _ => new[] { $"http://localhost:{port1}/base/path" });

                // Dynamic port
                dataset.Add("0", GetTestUrls);
                dataset.Add("http://localhost:0/", GetTestUrls);
                dataset.Add($"http://{Dns.GetHostName()}:0/", GetTestUrls);

                var ipv4Addresses = Dns.GetHostAddressesAsync(Dns.GetHostName()).Result
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork);
                foreach (var ip in ipv4Addresses)
                {
                    dataset.Add($"http://{ip}:0/", GetTestUrls);
                }

                return dataset;
            }
        }

        public static TheoryData<string, Func<IServerAddressesFeature, string[]>> AddressRegistrationDataIPv6
        {
            get
            {
                var dataset = new TheoryData<string, Func<IServerAddressesFeature, string[]>>();

                // Static port
                var port = GetNextPort();
                dataset.Add($"http://*:{port}/", _ => new[] { $"http://localhost:{port}/", $"http://127.0.0.1:{port}/", $"http://[::1]:{port}/" });
                dataset.Add($"http://localhost:{port}/", _ => new[] { $"http://localhost:{port}/", $"http://127.0.0.1:{port}/",
                    /* // https://github.com/aspnet/KestrelHttpServer/issues/231
                    $"http://[::1]:{port}/"
                    */ });
                dataset.Add($"http://[::1]:{port}/", _ => new[] { $"http://[::1]:{port}/", });
                dataset.Add($"http://127.0.0.1:{port}/;http://[::1]:{port}/", _ => new[] { $"http://127.0.0.1:{port}/", $"http://[::1]:{port}/" });

                // Dynamic port
                var ipv6Addresses = Dns.GetHostAddressesAsync(Dns.GetHostName()).Result
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetworkV6);
                foreach (var ip in ipv6Addresses)
                {
                    dataset.Add($"http://[{ip}]:0/", GetTestUrls);
                }

                return dataset;
            }
        }

        private static string[] GetTestUrls(IServerAddressesFeature addressesFeature)
        {
            return addressesFeature.Addresses
                .Select(a => a.StartsWith("http://+") ? a.Replace("http://+", "http://localhost") : a)
                .Select(a => a.EndsWith("/") ? a : a + "/")
                .ToArray();
        }

        private void ConfigureEchoAddress(IApplicationBuilder app)
        {
            app.Run(context =>
            {
                return context.Response.WriteAsync(context.Request.GetDisplayUrl());
            });
        }

        private static int _nextPort = 8001;
        private static object _portLock = new object();
        private static int GetNextPort()
        {
            lock (_portLock)
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    while (true)
                    {
                        try
                        {
                            var port = _nextPort++;
                            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                            return port;
                        }
                        catch (SocketException)
                        {
                            // Retry unless exhausted
                            if (_nextPort == 65536)
                            {
                                throw;
                            }
                        }
                    }
                }
            }
        }
    }
}
