// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions
{
    public interface ITransportTrace : ILogger
    {
        void ConnectionRead(string connectionId, int count);

        void ConnectionReadFin(string connectionId);

        void ConnectionWriteFin(string connectionId);

        void ConnectionWroteFin(string connectionId, int status);

        void ConnectionWriteCallback(string connectionId, int status);

        void ConnectionError(string connectionId, Exception ex);

        void ConnectionReset(string connectionId);

        void NotAllConnectionsClosedGracefully();

        void NotAllConnectionsAborted();
    }
}