// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;

namespace TransportChoiceApp
{
    public class Startup
    {
        private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");

        public void Configure(IApplicationBuilder app)
        {
            app.Run((httpContext) =>
            {
                var response = httpContext.Response;
                var payloadLength = _helloWorldPayload.Length;
                response.StatusCode = 200;
                response.ContentType = "text/plain";
                response.ContentLength = payloadLength;
                return response.Body.WriteAsync(_helloWorldPayload, 0, payloadLength);
            });
        }

        public static void Main(string[] args)
        {
            var hostBuilder = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, 5001);
                    
                });

            Console.WriteLine(@"
Choose a Transport
 1. Libuv
 2. Sockets
 3. Rio
");

            switch (Console.ReadKey().KeyChar)
            {
                case '1':
                    hostBuilder.UseLibuv(options =>
                    {
                        //options.ThreadCount = 4;
                    });
                    break;
                case '2':
                    hostBuilder.UseSockets();
                    break;
                case '3':
                    hostBuilder.UseWindowsRio();
                    break;
                default:
                    Console.WriteLine("Invalid option");
                    return;
            }

            Console.WriteLine();

            hostBuilder
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>();

            var host = hostBuilder.Build();

            host.Run();
        }
    }
}
