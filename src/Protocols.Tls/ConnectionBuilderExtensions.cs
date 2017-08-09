// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Protocols.Tls;

namespace Microsoft.AspNetCore.Protocols.Abstractions
{
    /// <summary>
    /// Extension methods from <see cref="IConnectionBuilder"/> that configure Kestrel to use HTTPS for a given endpoint.
    /// </summary>
    public static class ConnectionBuilderExtensions
    {
        /// <summary>
        /// Configure Kestrel to use HTTPS.
        /// </summary>
        /// <param name="connectionBuilder">
        /// The <see cref="IConnectionBuilder"/> to configure.
        /// </param>
        /// <param name="fileName">
        /// The name of a certificate file, relative to the directory that contains the application content files.
        /// </param>
        /// <returns>
        /// The <see cref="IConnectionBuilder"/>.
        /// </returns>
        public static IConnectionBuilder UseTls(this IConnectionBuilder connectionBuilder, string fileName)
        {
            // TODO: Resolve the right physical path using the hosting content root
            return connectionBuilder.UseTls(new X509Certificate2(fileName));
        }

        /// <summary>
        /// Configure Kestrel to use HTTPS.
        /// </summary>
        /// <param name="connectionBuilder">
        /// The <see cref="IConnectionBuilder"/> to configure.
        /// </param>
        /// <param name="fileName">
        /// The name of a certificate file, relative to the directory that contains the application content files.
        /// </param>
        /// <param name="password">
        /// The password required to access the X.509 certificate data.
        /// </param>
        /// <returns>
        /// The <see cref="IConnectionBuilder"/>.
        /// </returns>
        public static IConnectionBuilder UseTls(this IConnectionBuilder connectionBuilder, string fileName, string password)
        {
            // TODO: Resolve the right physical path using the hosting content root
            return connectionBuilder.UseTls(new X509Certificate2(fileName, password));
        }

        /// <summary>
        /// Configure Kestrel to use HTTPS.
        /// </summary>
        /// <param name="connectionBuilder">
        /// The <see cref="IConnectionBuilder"/> to configure.
        /// </param>
        /// <param name="serverCertificate">
        /// The X.509 certificate.
        /// </param>
        /// <returns>
        /// The <see cref="IConnectionBuilder"/>.
        /// </returns>
        public static IConnectionBuilder UseTls(this IConnectionBuilder connectionBuilder, X509Certificate2 serverCertificate)
        {
            return connectionBuilder.UseTls(new TlsConnectionOptions
            {
                ServerCertificate = serverCertificate
            });
        }

        /// <summary>
        /// Configure Kestrel to use HTTPS.
        /// </summary>
        /// <param name="connectionBuilder">
        /// The <see cref="IConnectionBuilder"/> to configure.
        /// </param>
        /// <param name="tlsOptions">
        /// Options to configure HTTPS.
        /// </param>
        /// <returns>
        /// The <see cref="IConnectionBuilder"/>.
        /// </returns>
        public static IConnectionBuilder UseTls(this IConnectionBuilder connectionBuilder, TlsConnectionOptions tlsOptions)
        {
            //var loggerFactory = IConnectionBuilder.ServerOptions.ApplicationServices.GetRequiredService<ILoggerFactory>();
            //IConnectionBuilder.ConnectionAdapters.Add();
            connectionBuilder.Use(next =>
            {
                var middleware = new TlsConnectionMiddleware(next, tlsOptions, null);
                return middleware.OnConnectionAsync;
            });

            return connectionBuilder;
        }
    }
}
