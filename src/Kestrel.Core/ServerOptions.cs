// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core
{
    /// <summary>
    /// Provides programmatic configuration of Kestrel-specific features.
    /// </summary>
    public class ServerOptions
    {
        /// <summary>
        /// Configures the endpoints that Kestrel should listen to.
        /// </summary>
        /// <remarks>
        /// If this list is empty, the server.urls setting (e.g. UseUrls) is used.
        /// </remarks>
        internal List<ListenOptions> ListenOptions { get; } = new List<ListenOptions>();

        /// <summary>
        /// Gets or sets whether the <c>Server</c> header should be included in each response.
        /// </summary>
        /// <remarks>
        /// Defaults to true.
        /// </remarks>
        // public bool AddServerHeader { get; set; } = true;

        /// <summary>
        /// Gets or sets a value that determines how Kestrel should schedule user callbacks.
        /// </summary>
        /// <remarks>The default mode is <see cref="SchedulingMode.Default"/></remarks>
        public SchedulingMode ApplicationSchedulingMode { get; set; } = SchedulingMode.Default;

        /// <summary>
        /// Gets or sets a value that controls whether synchronous IO is allowed 
        /// </summary>
        /// <remarks>
        /// Defaults to true.
        /// </remarks>
        public bool AllowSynchronousIO { get; set; } = true;

        /// <summary>
        /// Enables the Listen options callback to resolve and use services registered by the application during startup.
        /// Typically initialized by UseKestrel()"/>.
        /// </summary>
        public IServiceProvider ApplicationServices { get; set; }

        public IConnectionBuilder Listen(string hostname, int port)
        {
            // TODO: Implement
            return null;
        }

        /// <summary>
        /// Bind to given IP address and port.
        /// The callback configures endpoint-specific settings.
        /// </summary>
        public IConnectionBuilder Listen(IPAddress address, int port)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            return Listen(new IPEndPoint(address, port));
        }

        /// <summary>
        /// Bind to given IP address and port.
        /// The callback configures endpoint-specific settings.
        /// </summary>
        public IConnectionBuilder Listen(IPEndPoint endPoint)
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException(nameof(endPoint));
            }
            
            var listenOptions = new ListenOptions(endPoint) { ServerOptions = this };
            ListenOptions.Add(listenOptions);
            return listenOptions;
        }

        /// <summary>
        /// Bind to given Unix domain socket path.
        /// Specify callback to configure endpoint-specific settings.
        /// </summary>
        public IConnectionBuilder ListenUnixSocket(string socketPath)
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
            ListenOptions.Add(listenOptions);
            return listenOptions;
        }
        
        /// <summary>
        /// Open a socket file descriptor.
        /// The callback configures endpoint-specific settings.
        /// </summary>
        public IConnectionBuilder ListenHandle(ulong handle)
        {
            var listenOptions = new ListenOptions(handle) { ServerOptions = this };
            ListenOptions.Add(listenOptions);
            return listenOptions;
        }
    }
}
