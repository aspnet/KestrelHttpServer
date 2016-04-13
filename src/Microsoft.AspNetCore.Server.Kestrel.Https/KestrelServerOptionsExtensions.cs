// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Filter;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Server.Kestrel
{
    public static class KestrelServerOptionsExtensions
    {
        public static KestrelServerOptions UseHttps(this KestrelServerOptions options, string fileName, string password)
        {
            var env = options.ApplicationServices.GetRequiredService<IHostingEnvironment>();
            return options.UseHttps(new X509Certificate2(Path.Combine(env.ContentRootPath, fileName), password));
        }

        public static KestrelServerOptions UseHttps(this KestrelServerOptions options, X509Certificate2 serverCertificate)
        {
            return options.UseHttps(new HttpsConnectionFilterOptions { ServerCertificate = serverCertificate });
        }

        public static KestrelServerOptions UseHttps(this KestrelServerOptions options, HttpsConnectionFilterOptions httpsOptions)
        {
            var prevFilter = options.ConnectionFilter ?? new NoOpConnectionFilter();
            options.ConnectionFilter = new HttpsConnectionFilter(httpsOptions, prevFilter);
            return options;
        }
    }
}
