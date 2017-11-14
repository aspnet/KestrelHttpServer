﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core
{
    /// <summary>
    /// Describes either an <see cref="IPEndPoint"/>, Unix domain socket path, or a file descriptor for an already open
    /// socket that Kestrel should bind to or open.
    /// </summary>
    public class ListenOptions : IEndPointInformation, IConnectionBuilder
    {
        private FileHandleType _handleType;
        private readonly List<Func<ConnectionDelegate, ConnectionDelegate>> _components = new List<Func<ConnectionDelegate, ConnectionDelegate>>();

        internal ListenOptions(ListenType type)
        {
            Type = type;
        }

        internal ListenOptions(IPEndPoint endPoint)
            : this(ListenType.IPEndPoint)
        {
            IPEndPoint = endPoint;
        }

        internal ListenOptions(string socketPath)
            : this(ListenType.SocketPath)
        {
            SocketPath = socketPath;
        }

        internal ListenOptions(ulong fileHandle)
            : this(fileHandle, FileHandleType.Auto)
        {
        }

        internal ListenOptions(ulong fileHandle, FileHandleType handleType)
            : this(ListenType.FileHandle)
        {
            FileHandle = fileHandle;
            switch (handleType)
            {
                case FileHandleType.Auto:
                case FileHandleType.Tcp:
                case FileHandleType.Pipe:
                    _handleType = handleType;
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        /// <summary>
        /// The type of interface being described: either an <see cref="IPEndPoint"/>, Unix domain socket path, or a file descriptor.
        /// </summary>
        public ListenType Type { get; internal set; }

        public FileHandleType HandleType
        {
            get => _handleType;
            set
            {
                if (value == _handleType)
                {
                    return;
                }
                if (Type != ListenType.FileHandle || _handleType != FileHandleType.Auto)
                {
                    throw new InvalidOperationException();
                }

                switch (value)
                {
                    case FileHandleType.Tcp:
                    case FileHandleType.Pipe:
                        _handleType = value;
                        break;
                    default:
                        throw new ArgumentException(nameof(HandleType));
                }
            }
        }

        // IPEndPoint is mutable so port 0 can be updated to the bound port.
        /// <summary>
        /// The <see cref="IPEndPoint"/> to bind to.
        /// Only set if the <see cref="ListenOptions"/> <see cref="Type"/> is <see cref="ListenType.IPEndPoint"/>.
        /// </summary>
        public IPEndPoint IPEndPoint { get; set; }

        internal string Prefix { get; set; }

        /// <summary>
        /// The absolute path to a Unix domain socket to bind to.
        /// Only set if the <see cref="ListenOptions"/> <see cref="Type"/> is <see cref="ListenType.SocketPath"/>.
        /// </summary>
        public string SocketPath { get; internal set; }

        /// <summary>
        /// A file descriptor for the socket to open.
        /// Only set if the <see cref="ListenOptions"/> <see cref="Type"/> is <see cref="ListenType.FileHandle"/>.
        /// </summary>
        public ulong FileHandle { get; internal set; }

        /// <summary>
        /// Enables an <see cref="IConnectionAdapter"/> to resolve and use services registered by the application during startup.
        /// Only set if accessed from the callback of a <see cref="KestrelServerOptions"/> Listen* method.
        /// </summary>
        public KestrelServerOptions KestrelServerOptions { get; internal set; }

        /// <summary>
        /// Set to false to enable Nagle's algorithm for all connections.
        /// </summary>
        /// <remarks>
        /// Defaults to true.
        /// </remarks>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// The protocols enabled on this endpoint.
        /// </summary>
        /// <remarks>Defaults to HTTP/1.x only.</remarks>
        public HttpProtocols Protocols { get; set; } = HttpProtocols.Http1;

        /// <summary>
        /// Gets the <see cref="List{IConnectionAdapter}"/> that allows each connection <see cref="System.IO.Stream"/>
        /// to be intercepted and transformed.
        /// Configured by the <c>UseHttps()</c> and <see cref="Hosting.ListenOptionsConnectionLoggingExtensions.UseConnectionLogging(ListenOptions)"/>
        /// extension methods.
        /// </summary>
        /// <remarks>
        /// Defaults to empty.
        /// </remarks>
        public List<IConnectionAdapter> ConnectionAdapters { get; } = new List<IConnectionAdapter>();

        public IServiceProvider ApplicationServices => KestrelServerOptions?.ApplicationServices;

        /// <summary>
        /// Gets the name of this endpoint to display on command-line when the web server starts.
        /// </summary>
        internal string GetDisplayName()
        {
            var scheme = ConnectionAdapters.Any(f => f.IsHttps)
                ? "https"
                : "http";

            switch (Type)
            {
                case ListenType.Prefix:
                    return Prefix;
                case ListenType.IPEndPoint:
                    return $"{scheme}://{IPEndPoint}";
                case ListenType.SocketPath:
                    return $"{scheme}://unix:{SocketPath}";
                case ListenType.FileHandle:
                    return $"{scheme}://<file handle>";
                default:
                    throw new InvalidOperationException();
            }
        }

        public override string ToString() => GetDisplayName();

        public IConnectionBuilder Use(Func<ConnectionDelegate, ConnectionDelegate> middleware)
        {
            _components.Add(middleware);
            return this;
        }

        public ConnectionDelegate Build()
        {
            ConnectionDelegate app = context =>
            {
                return Task.CompletedTask;
            };

            for (int i = _components.Count - 1; i >= 0; i--)
            {
                var component = _components[i];
                app = component(app);
            }

            return app;
        }

        // primarily used for cloning localhost to two IPEndpoints
        internal ListenOptions CloneAs(ListenType type)
        {
            var options = new ListenOptions(type)
            {
                HandleType = HandleType,
                IPEndPoint = IPEndPoint,
                KestrelServerOptions = KestrelServerOptions,
                NoDelay = NoDelay,
                Prefix = Prefix,
                Protocols = Protocols,
            };
            options.ConnectionAdapters.AddRange(ConnectionAdapters);
            return options;
        }
    }
}
