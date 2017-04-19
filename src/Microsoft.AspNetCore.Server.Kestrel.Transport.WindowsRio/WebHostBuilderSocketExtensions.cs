﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Hosting
{
    public static class WebHostBuilderSocketExtensions
    {
        /// <summary>
        /// Specify Winsock Registered I/O as the transport to be used by Kestrel.
        /// </summary>
        /// <param name="hostBuilder">
        /// The Microsoft.AspNetCore.Hosting.IWebHostBuilder to configure.
        /// </param>
        /// <returns>
        /// The Microsoft.AspNetCore.Hosting.IWebHostBuilder.
        /// </returns>
        public static IWebHostBuilder UseWindowsRio(this IWebHostBuilder hostBuilder)
        {
            return hostBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<ITransportFactory, WindowsRioTransportFactory>();
            });
        }
    }
}
