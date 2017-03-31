// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    public class SocketTransportFactory : ITransportFactory
    {
        public SocketTransportFactory()
        {
        }

        public ITransport Create(ListenOptions listenOptions, IConnectionHandler handler)
        {
            return new SocketTransport(listenOptions, handler);
        }
    }
}
