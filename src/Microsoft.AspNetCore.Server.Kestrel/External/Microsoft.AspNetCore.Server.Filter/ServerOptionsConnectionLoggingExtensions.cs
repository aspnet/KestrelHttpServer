// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Server.Abstractions;
using Microsoft.AspNetCore.Server.Filter;

namespace Microsoft.AspNetCore.Hosting
{
    public static class ServerOptionsConnectionLoggingExtensions
    {
        /// <summary>
        /// Emits verbose logs for bytes read from and written to the connection.
        /// </summary>
        /// <returns>
        /// The Microsoft.AspNetCore.Server.KestrelServerOptions.
        /// </returns>
        public static ServerOptions UseConnectionLogging(this ServerOptions options)
        {
            return options.UseConnectionLogging(nameof(LoggingConnectionFilter));
        }

        /// <summary>
        /// Emits verbose logs for bytes read from and written to the connection.
        /// </summary>
        /// <returns>
        /// The Microsoft.AspNetCore.Server.KestrelServerOptions.
        /// </returns>
        public static ServerOptions UseConnectionLogging(this ServerOptions options, string loggerName)
        {
            var prevFilter = options.ConnectionFilter ?? new NoOpConnectionFilter();
            var loggerFactory = options.ApplicationServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(loggerName ?? nameof(LoggingConnectionFilter));
            options.ConnectionFilter = new LoggingConnectionFilter(logger, prevFilter);
            return options;
        }
    }
}
