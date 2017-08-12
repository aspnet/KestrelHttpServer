// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Core
{
    /// <summary>
    /// Provides programmatic configuration of Kestrel-specific features.
    /// </summary>
    public class ServerBuilder
    {
        /// <summary>
        /// Configures the endpoints that Kestrel should listen to.
        /// </summary>
        internal List<ListenOptions> ListenOptions { get; } = new List<ListenOptions>();

        internal ITransportFactory TransportFactory { get; set; }

        internal ILoggerFactory LoggerFactory { get; set; }

        /// <summary>
        /// Enables the Listen options callback to resolve and use services registered by the application during startup.
        /// Typically initialized by UseKestrel()"/>.
        /// </summary>
        public IServiceProvider ApplicationServices { get; set; }

        public ServerBuilder Listen(string hostname, int port, Action<IConnectionBuilder> configure)
        {
            // TODO: Implement
            return null;
        }

        /// <summary>
        /// Bind to given IP address and port.
        /// The callback configures endpoint-specific settings.
        /// </summary>
        public ServerBuilder Listen(IPAddress address, int port, Action<IConnectionBuilder> configure)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            return Listen(new IPEndPoint(address, port), configure);
        }

        /// <summary>
        /// Bind to given IP address and port.
        /// The callback configures endpoint-specific settings.
        /// </summary>
        public ServerBuilder Listen(IPEndPoint endPoint, Action<IConnectionBuilder> configure)
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException(nameof(endPoint));
            }

            var listenOptions = new ListenOptions(endPoint) { ServerOptions = this };
            configure(listenOptions);
            ListenOptions.Add(listenOptions);
            return this;
        }

        /// <summary>
        /// Bind to given Unix domain socket path.
        /// Specify callback to configure endpoint-specific settings.
        /// </summary>
        public ServerBuilder ListenUnixSocket(string socketPath, Action<IConnectionBuilder> configure)
        {
            if (socketPath == null)
            {
                throw new ArgumentNullException(nameof(socketPath));
            }
            if (socketPath.Length == 0 || socketPath[0] != '/')
            {
                throw new ArgumentException(CoreStrings.UnixSocketPathMustBeAbsolute, nameof(socketPath));
            }

            var listenOptions = new ListenOptions(socketPath) { ServerOptions = this };
            configure(listenOptions);
            ListenOptions.Add(listenOptions);
            return this;
        }

        /// <summary>
        /// Open a socket file descriptor.
        /// The callback configures endpoint-specific settings.
        /// </summary>
        public ServerBuilder ListenHandle(ulong handle, Action<IConnectionBuilder> configure)
        {
            var listenOptions = new ListenOptions(handle) { ServerOptions = this };
            configure(listenOptions);
            ListenOptions.Add(listenOptions);
            return this;
        }

        public Server Build()
        {
            // Where do we get this from?
            return new Server(ListenOptions, TransportFactory, LoggerFactory);
        }
    }
}
