// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Server.Infrastructure;

namespace Microsoft.AspNetCore.Server.Abstractions
{
    public interface IConnectionTrace : ILogger
    {
        void ConnectionStart(string connectionId);

        void ConnectionStop(string connectionId);

        void ConnectionRead(string connectionId, int count);

        void ConnectionPause(string connectionId);

        void ConnectionResume(string connectionId);

        void ConnectionReadFin(string connectionId);

        void ConnectionWriteFin(string connectionId);

        void ConnectionWroteFin(string connectionId, int status);

        void ConnectionKeepAlive(string connectionId);

        void ConnectionDisconnect(string connectionId);

        void ConnectionWrite(string connectionId, int count);

        void ConnectionWriteCallback(string connectionId, int status);

        void ConnectionError(string connectionId, Exception ex);

        void ConnectionDisconnectedWrite(string connectionId, int count, Exception ex);

        void ConnectionBadRequest(string connectionId, BadHttpRequestException ex);

        void NotAllConnectionsClosedGracefully();

        void ApplicationError(string connectionId, Exception ex);
    }
}