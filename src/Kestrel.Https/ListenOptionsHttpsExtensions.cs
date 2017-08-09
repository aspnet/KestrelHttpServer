// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace Microsoft.AspNetCore.Hosting
{
    /// <summary>
    /// Extension methods fro <see cref="ListenOptions"/> that configure Kestrel to use HTTPS for a given endpoint.
    /// </summary>
    public static class ListenOptionsHttpsExtensions
    {
        /// <summary>
        /// Configure Kestrel to use HTTPS.
        /// </summary>
        /// <param name="listenOptions">
        /// The <see cref="ListenOptions"/> to configure.
        /// </param>
        /// <param name="fileName">
        /// The name of a certificate file, relative to the directory that contains the application content files.
        /// </param>
        /// <returns>
        /// The <see cref="ListenOptions"/>.
        /// </returns>
        public static IConnectionBuilder UseTls(this IConnectionBuilder listenOptions, string fileName)
        {
            // TODO: Resolve the right physical path using the hosting content root
            return listenOptions.UseTls(new X509Certificate2(fileName));
        }

        /// <summary>
        /// Configure Kestrel to use HTTPS.
        /// </summary>
        /// <param name="listenOptions">
        /// The <see cref="ListenOptions"/> to configure.
        /// </param>
        /// <param name="fileName">
        /// The name of a certificate file, relative to the directory that contains the application content files.
        /// </param>
        /// <param name="password">
        /// The password required to access the X.509 certificate data.
        /// </param>
        /// <returns>
        /// The <see cref="ListenOptions"/>.
        /// </returns>
        public static IConnectionBuilder UseTls(this IConnectionBuilder listenOptions, string fileName, string password)
        {
            // TODO: Resolve the right physical path using the hosting content root
            return listenOptions.UseTls(new X509Certificate2(fileName, password));
        }

        /// <summary>
        /// Configure Kestrel to use HTTPS.
        /// </summary>
        /// <param name="listenOptions">
        /// The <see cref="ListenOptions"/> to configure.
        /// </param>
        /// <param name="serverCertificate">
        /// The X.509 certificate.
        /// </param>
        /// <returns>
        /// The <see cref="ListenOptions"/>.
        /// </returns>
        public static IConnectionBuilder UseTls(this IConnectionBuilder listenOptions, X509Certificate2 serverCertificate)
        {
            return listenOptions.UseTls(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = serverCertificate
            });
        }

        /// <summary>
        /// Configure Kestrel to use HTTPS.
        /// </summary>
        /// <param name="listenOptions">
        /// The <see cref="ListenOptions"/> to configure.
        /// </param>
        /// <param name="httpsOptions">
        /// Options to configure HTTPS.
        /// </param>
        /// <returns>
        /// The <see cref="ListenOptions"/>.
        /// </returns>
        public static IConnectionBuilder UseTls(this IConnectionBuilder listenOptions, HttpsConnectionAdapterOptions httpsOptions)
        {
            //var loggerFactory = listenOptions.ServerOptions.ApplicationServices.GetRequiredService<ILoggerFactory>();
            //listenOptions.ConnectionAdapters.Add(new HttpsConnectionAdapter(httpsOptions, loggerFactory));
            return listenOptions;
        }
    }
}
