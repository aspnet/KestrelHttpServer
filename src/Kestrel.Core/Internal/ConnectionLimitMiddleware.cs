using System.Threading.Tasks;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public class ConnectionLimitMiddleware
    {
        private readonly ConnectionDelegate _next;
        private readonly IKestrelTrace _trace;
        private readonly ResourceCounter _normalConnectionCount;

        public ConnectionLimitMiddleware(ConnectionDelegate next, IKestrelTrace trace, long connectionLimit)
        {
            _next = next;
            _trace = trace;
            _normalConnectionCount = ResourceCounter.Quota(connectionLimit);
        }

        public async Task OnConnectionAsync(ConnectionContext connection)
        {
            if (!_normalConnectionCount.TryLockOne())
            {
                KestrelEventSource.Log.ConnectionRejected(connection.ConnectionId);
                _trace?.ConnectionRejected(connection.ConnectionId);
                connection.Transport.Input.Complete();
                connection.Transport.Output.Complete();
                return;
            }

            try
            {
                await _next(connection);
            }
            finally
            {
                _normalConnectionCount.ReleaseOne();
            }
        }
    }
}
