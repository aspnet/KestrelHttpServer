// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Hosting
{
    public static class ListenOptionsConnectionLoggingExtensions
    {
        /// <summary>
        /// Emits verbose logs for bytes read from and written to the connection.
        /// </summary>
        /// <returns>
        /// The <see cref="ListenOptions"/>.
        /// </returns>
        public static ListenOptions UseConnectionLogging(this ListenOptions listenOptions)
        {
            return listenOptions.UseConnectionLogging(nameof(LoggingConnectionAdapter));
        }

        /// <summary>
        /// Emits verbose logs for bytes read from and written to the connection.
        /// </summary>
        /// <returns>
        /// The <see cref="ListenOptions"/>.
        /// </returns>
        public static ListenOptions UseConnectionLogging(this ListenOptions listenOptions, string loggerName)
        {
            var loggerFactory = listenOptions.KestrelServerOptions.ApplicationServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(loggerName ?? nameof(LoggingConnectionAdapter));
            listenOptions.ConnectionAdapters.Add(new LoggingConnectionAdapter(logger));
            return listenOptions;
        }
    }
}
