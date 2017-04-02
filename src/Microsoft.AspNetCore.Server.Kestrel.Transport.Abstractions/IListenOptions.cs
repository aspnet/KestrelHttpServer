// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Adapter;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions
{
    public interface IListenOptions
    {
        /// <summary>
        /// The type of interface being described: either an <see cref="IPEndPoint"/>, Unix domain socket path, or a file descriptor.
        /// </summary>
        ListenType Type { get; }

        // IPEndPoint is mutable so port 0 can be updated to the bound port.
        /// <summary>
        /// The <see cref="IPEndPoint"/> to bind to.
        /// Only set if <see cref="Type"/> is <see cref="ListenType.IPEndPoint"/>.
        /// </summary>
        IPEndPoint IPEndPoint { get; set; }

        /// <summary>
        /// The absolute path to a Unix domain socket to bind to.
        /// Only set if <see cref="Type"/> is <see cref="ListenType.SocketPath"/>.
        /// </summary>
        string SocketPath { get; }

        /// <summary>
        /// A file descriptor for the socket to open.
        /// Only set if <see cref="Type"/> is <see cref="ListenType.FileHandle"/>.
        /// </summary>
        ulong FileHandle { get; }

        /// <summary>
        /// Set to false to enable Nagle's algorithm for all connections.
        /// </summary>
        bool NoDelay { get; }

        /// <summary>
        /// Gets the <see cref="List{IConnectionAdapter}"/> that allows each connection <see cref="System.IO.Stream"/>
        /// to be intercepted and transformed.
        /// </summary>
        List<IConnectionAdapter> ConnectionAdapters { get; }

        // Scheme is hopefully only a temporary measure for back compat with IServerAddressesFeature.
        // TODO: Allow connection adapters to configure the scheme
        string Scheme { get; set; }
    }
}
