// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Options.Infrastructure;

namespace Microsoft.AspNetCore.Hosting
{
    public static class KestrelConfigurationExtensions
    {
        /// <summary>
        /// Specify Kestrel as the server to be used by the web host and bind its settings from configuration.
        /// </summary>
        /// <param name="hostBuilder">
        /// The Microsoft.AspNetCore.Hosting.IWebHostBuilder to configure.
        /// </param>
        /// <returns>
        /// The Microsoft.AspNetCore.Hosting.IWebHostBuilder.
        /// </returns>
        public static IWebHostBuilder UseKestrelWithConfiguration(this IWebHostBuilder hostBuilder)
        {
            hostBuilder.UseKestrel();
            return hostBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<ConfigureDefaultOptions<KestrelServerOptions>, KestrelServerConfigureOptions>();
            });
        }
    }
}
