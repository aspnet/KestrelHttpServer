using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure
{
    public interface IKestrelTrace : ILogger
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

        void ConnectionReset(string connectionId);

        void RequestProcessingError(string connectionId, Exception ex);

        void ConnectionDisconnectedWrite(string connectionId, int count, Exception ex);

        void ConnectionHeadResponseBodyWrite(string connectionId, long count);

        void ConnectionBadRequest(string connectionId, BadHttpRequestException ex);

        void NotAllConnectionsClosedGracefully();

        void NotAllConnectionsAborted();

        void ApplicationError(string connectionId, Exception ex);
    }
}