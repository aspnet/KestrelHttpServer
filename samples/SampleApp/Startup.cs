// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SampleApp
{
    public class Startup
    {
        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger("Default");

            app.Run(async context =>
            {
                var connectionFeature = context.Connection;
                logger.LogDebug($"Peer: {connectionFeature.RemoteIpAddress?.ToString()}:{connectionFeature.RemotePort}"
                    + $"{Environment.NewLine}"
                    + $"Sock: {connectionFeature.LocalIpAddress?.ToString()}:{connectionFeature.LocalPort}");

                var response = $"hello, world{Environment.NewLine}";
                context.Response.ContentLength = response.Length;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(response);
            });
        }

        public static async Task Main(string[] args)
        {
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Console.WriteLine("Unobserved exception: {0}", e.Exception);
            };

            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            if (!ushort.TryParse(configuration["BASE_PORT"], NumberStyles.None, CultureInfo.InvariantCulture, out var basePort))
            {
                basePort = 5000;
            }


            var server = new ServerBuilder()
                    .Listen("localhost", 8085, builder =>
                    {
                        builder.UseTls("foo.pfx")
                            .UseHttpServer(app =>
                            {
                                app.Run(async context =>
                                 {
                                     await context.Response.WriteAsync("Hello World");
                                 });
                            });
                            
                    })
                    .Listen(IPAddress.Any, 5001, builder =>
                    {
                        builder.UseConnectionLogging()
                            .UseHttpServer<Startup>();
                    })
                    .Build();

            await server.StartAsync();

            Console.ReadLine();

            await server.StopAsync();

            var host = new WebHostBuilder()
                .ConfigureLogging((_, factory) =>
                {
                    factory.AddConsole();
                })
                .UseKestrel(options =>
                {
                    // Run callbacks on the transport thread
                    // options.ApplicationSchedulingMode = SchedulingMode.Inline;

                    options.Listen(IPAddress.Loopback, basePort, listenOptions =>
                    {
                        // Uncomment the following to enable Nagle's algorithm for this endpoint.
                        //listenOptions.NoDelay = false;

                        listenOptions.UseConnectionLogging();
                    });

                    options.Listen(IPAddress.Loopback, basePort + 1, listenOptions =>
                    {
                        // listenOptions.UseHttps("testCert.pfx", "testPassword");
                        listenOptions.UseConnectionLogging();
                    });

                    // options.UseSystemd();

                    // The following section should be used to demo sockets
                    //options.ListenUnixSocket("/tmp/kestrel-test.sock");
                })
                .UseLibuv(options =>
                {
                    // Uncomment the following line to change the default number of libuv threads for all endpoints.
                    // options.ThreadCount = 4;
                })
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>()
                .Build();

            host.Run();
        }
    }
}