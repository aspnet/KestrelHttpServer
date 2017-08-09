// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public class ConnectionHandler : IConnectionHandler
    {
        private readonly ConnectionDelegate _connectionDelegate;

        public ConnectionHandler(ListenOptions listenOptions)
        {
            _connectionDelegate = listenOptions.Build();
        }

        public void OnConnection(ConnectionContext connection)
        {
            // Since data cannot be added to the inputPipe by the transport until OnConnection returns,
            // Frame.ProcessRequestsAsync is guaranteed to unblock the transport thread before calling
            // application code.
            _ = _connectionDelegate(connection);
        }
    }
}
