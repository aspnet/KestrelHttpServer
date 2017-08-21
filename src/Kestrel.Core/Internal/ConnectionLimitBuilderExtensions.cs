using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public static class ConnectionLimitBuilderExtensions
    {
        public static IConnectionBuilder UseConnectionLimit(this IConnectionBuilder builder, IKestrelTrace trace, long connectionLimit)
        {
            return builder.Use(next =>
            {
                var middleware = new ConnectionLimitMiddleware(next, trace, connectionLimit);
                return middleware.OnConnectionAsync;
            });
        }
    }
}
