// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    public class SocketTransportFactory : ITransportFactory
    {
        public SocketTransportFactory()
        {
        }

        public ITransport Create(IEndPointInformation endPointInformation, IConnectionHandler handler)
        {
            return new SocketTransport(endPointInformation, handler);
        }
    }
}
