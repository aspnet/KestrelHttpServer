// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SampleApp
{
    public class Startup
    {
        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            // loggerFactory.AddConsole(LogLevel.Trace);
            // var logger = loggerFactory.CreateLogger("Default");

            var data = Encoding.UTF8.GetBytes($"hello, world{Environment.NewLine}");

            app.Run(async context =>
            {
                //var connectionFeature = context.Connection;
                //logger.LogDebug($"Peer: {connectionFeature.RemoteIpAddress?.ToString()}:{connectionFeature.RemotePort}"
                //    + $"{Environment.NewLine}"
                //    + $"Sock: {connectionFeature.LocalIpAddress?.ToString()}:{connectionFeature.LocalPort}");

                context.Response.ContentLength = data.Length;
                context.Response.ContentType = "text/plain";
                await context.Response.Body.WriteAsync(data, 0, data.Length);
            });
        }

        public static void Main(string[] args)
        {
            var host = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    options.Listen(IPAddress.Any, 5000, listenOptions =>
                    {
                        // Uncomment the following to enable Nagle's algorithm for this endpoint.
                        //listenOptions.NoDelay = false;

                        // listenOptions.UseConnectionLogging();
                    });
                    //options.Listen(IPAddress.Loopback, 5001, listenOptions =>
                    //{
                    //    listenOptions.UseHttps("testCert.pfx", "testPassword");
                    //    listenOptions.UseConnectionLogging();
                    //});

                    options.UseSystemd();

                    // The following section should be used to demo sockets
                    //options.ListenUnixSocket("/tmp/kestrel-test.sock");

                    // Uncomment the following line to change the default number of libuv threads for all endpoints.
                    //options.ThreadCount = 4;
                })
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>()
                .Build();

            host.Run();
        }
    }
}