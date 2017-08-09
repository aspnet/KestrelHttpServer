using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;

namespace Microsoft.AspNetCore.Protocols.Abstractions
{
    public static class HttpServerConnectionBuilderExtensions
    {
        public static IConnectionBuilder UseHttpServer<TStartup>(this IConnectionBuilder connectionBuilder)
        {
            // INCEPTION!
            return connectionBuilder;
        }

        public static IConnectionBuilder UseHttpServer(this IConnectionBuilder connectionBuilder, Action<IApplicationBuilder> configure)
        {
            return connectionBuilder;
        }

        public static IConnectionBuilder UseHttpServer<TContext>(this IConnectionBuilder connectionBuilder, IHttpApplication<TContext> application)
        {
            return connectionBuilder.Use(next =>
            {
                return async connection =>
                {
                    // TODO: Make it work
                    var context = new FrameConnectionContext();
                    var frameConnection = new FrameConnection(context);
                    await frameConnection.StartRequestProcessing(application);
                };
            });
        }
    }
}
