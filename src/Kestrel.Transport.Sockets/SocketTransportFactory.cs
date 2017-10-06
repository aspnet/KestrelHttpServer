﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    public sealed class SocketTransportFactory : ITransportFactory
    {
        private readonly PipeFactory _pipeFactory = new PipeFactory();
        private readonly SocketsTrace _trace;

        public SocketTransportFactory(
            IOptions<SocketTransportOptions> options,
            ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            var logger  = loggerFactory.CreateLogger("Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets");
            _trace = new SocketsTrace(logger);
        }

        public ITransport Create(IEndPointInformation endPointInformation, IConnectionHandler handler)
        {
            if (endPointInformation == null)
            {
                throw new ArgumentNullException(nameof(endPointInformation));
            }

            if (endPointInformation.Type != ListenType.IPEndPoint)
            {
                throw new ArgumentException(SocketsStrings.OnlyIPEndPointsSupported, nameof(endPointInformation));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return new SocketTransport(this, endPointInformation, handler, _trace);
        }

        internal PipeFactory PipeFactory => _pipeFactory;
    }
}
