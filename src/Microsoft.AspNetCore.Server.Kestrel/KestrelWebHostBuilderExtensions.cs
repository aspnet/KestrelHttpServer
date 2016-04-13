// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Hosting
{
    public static class WebHostBuilderKestrelExtensions
    {
        public static IWebHostBuilder UseKestrel(this IWebHostBuilder hostBuilder)
        {
            return hostBuilder.ConfigureServices(services => services.AddSingleton<IServerFactory, ServerFactory>());
        }

        public static IWebHostBuilder UseKestrel(this IWebHostBuilder hostBuilder, Action<KestrelServerOptions> options)
        {
            hostBuilder.ConfigureServices(services =>
            {
                services.AddTransient<IConfigureOptions<KestrelServerOptions>, KestrelServerOptionsSetup>();
                services.Configure(options);
            });

            return hostBuilder.UseKestrel();
        }
    }
}
