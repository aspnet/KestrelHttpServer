using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public static class HttpConnectionBuilderExtensions
    {
        public static IConnectionBuilder UseHttpServer<TContext>(this IConnectionBuilder builder, ServiceContext serviceContext, IHttpApplication<TContext> application)
        {
            return builder.UseHttpServer(serviceContext, application, protocols: HttpProtocols.Http1);
        }

        public static IConnectionBuilder UseHttpServer<TContext>(this IConnectionBuilder builder, ServiceContext serviceContext, IHttpApplication<TContext> application, HttpProtocols protocols)
        {
            return builder.UseHttpServer(Array.Empty<IConnectionAdapter>(), serviceContext, application, protocols);
        }

        public static IConnectionBuilder UseHttpServer<TContext>(this IConnectionBuilder builder, IList<IConnectionAdapter> adapters, ServiceContext serviceContext, IHttpApplication<TContext> application)
        {
            return builder.UseHttpServer(adapters, serviceContext, application, protocols: HttpProtocols.Http1);
        }

        public static IConnectionBuilder UseHttpServer<TContext>(this IConnectionBuilder builder, IList<IConnectionAdapter> adapters, ServiceContext serviceContext, IHttpApplication<TContext> application, HttpProtocols protocols)
        {
            var middleware = new HttpConnectionMiddleware<TContext>(adapters, serviceContext, application, protocols);
            return builder.Use(next =>
            {
                return middleware.OnConnectionAsync;
            });
        }
    }
}
