// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SampleApp
{
    public class Startup
    {

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<FormOptions>(x =>
            {
                x.ValueLengthLimit = int.MaxValue;
                x.MultipartBodyLengthLimit = int.MaxValue; // In case of multipart
                x.BufferBody = false;
            });
        }

        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            app.Run(Handler);
        }

        private static async Task Handler(HttpContext context)
        {
            if (context.Request.HasFormContentType)
            {
                var stream = context.Request.Body;
           //     stream = context.Request.Form.Files[0].OpenReadStream();

                var buffer = new byte[128];
                int bytesRead;
                do
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                } while (bytesRead != 0);
            }
            var response = @"<form enctype=""multipart/form-data"" action=""/"" method=""POST"">
<input type=""hidden"" name=""MAX_FILE_SIZE"" value=""100000"" />
Choose a file to upload: <input name=""uploadedfile"" type=""file"" /><br />
<input type=""submit"" value=""Upload File"" />
</form>";
            context.Response.ContentLength = response.Length;
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(response);

        }

        public static void Main(string[] args)
        {
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Console.WriteLine("Unobserved exception: {0}", e.Exception);
            };

            var host = new WebHostBuilder()
                .ConfigureLogging(factory =>
                {
                    factory.AddConsole();
                })
                .UseKestrel(options =>
                {
#if !NETCOREAPP1_1
                    options.Limits.MaxRequestBodySize = null;
#endif
                    options.Limits.MaxRequestBufferSize = null;
                })
                .UseUrls("http://*:5000")
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>()
                .Build();

            host.Run();
        }
    }
}