// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure
{
    public interface IKestrelTrace : ITransportTrace
    {
        void ConnectionStart(string connectionId);

        void ConnectionStop(string connectionId);

        void ConnectionPause(string connectionId);

        void ConnectionResume(string connectionId);

        void ConnectionKeepAlive(string connectionId);

        void ConnectionDisconnect(string connectionId);

        void ConnectionWrite(string connectionId, int count);

        void RequestProcessingError(string connectionId, Exception ex);

        void ConnectionDisconnectedWrite(string connectionId, int count, Exception ex);

        void ConnectionHeadResponseBodyWrite(string connectionId, long count);

        void ConnectionBadRequest(string connectionId, BadHttpRequestException ex);

        void ApplicationError(string connectionId, string traceIdentifier, Exception ex);
    }
}