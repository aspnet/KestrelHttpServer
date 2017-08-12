// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;

namespace Microsoft.AspNetCore.Hosting
{
    public static class ConnectionLoggingMiddlewareExtensions
    {
        /// <summary>
        /// Emits verbose logs for bytes read from and written to the connection.
        /// </summary>
        /// <returns>
        /// The <see cref="IConnectionBuilder"/>.
        /// </returns>
        public static IConnectionBuilder UseConnectionLogging(this IConnectionBuilder connectionBuilder)
        {
            return connectionBuilder.UseConnectionLogging(nameof(LoggingConnectionMiddleware));
        }

        /// <summary>
        /// Emits verbose logs for bytes read from and written to the connection.
        /// </summary>
        /// <returns>
        /// The <see cref="IConnectionBuilder"/>.
        /// </returns>
        public static IConnectionBuilder UseConnectionLogging(this IConnectionBuilder connectionBuilder, string loggerName)
        {
            connectionBuilder.Use(next =>
            {
                var adapter = new LoggingConnectionMiddleware(next, null);
                return adapter.OnConnectionAsync;
            });
            return connectionBuilder;
        }
    }
}
