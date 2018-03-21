// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Connections
{
    public static class ConnectionBuilderExtensions
    {
        public static IConnectionBuilder UseConnectionHandler<TConnectionHandler>(this IConnectionBuilder connectionBuilder) where TConnectionHandler : ConnectionHandler
        {
            // REVIEW: We should use ActivatorUtilities here
            var endpoint = (TConnectionHandler)connectionBuilder.ApplicationServices.GetService(typeof(TConnectionHandler));

            if (endpoint == null)
            {
                throw new InvalidOperationException($"{nameof(ConnectionHandler)} type {typeof(TConnectionHandler)} is not registered.");
            }
            // This is a terminal middleware, so there's no need to use the 'next' parameter
            return connectionBuilder.Run(connection => endpoint.OnConnectedAsync(connection));
        }

        public static IConnectionBuilder Use(this IConnectionBuilder connectionBuilder, Func<ConnectionContext, Func<Task>, Task> middleware)
        {
            return connectionBuilder.Use(next =>
            {
                return context =>
                {
                    Func<Task> simpleNext = () => next(context);
                    return middleware(context, simpleNext);
                };
            });
        }

        public static IConnectionBuilder Run(this IConnectionBuilder connectionBuilder, Func<ConnectionContext, Task> middleware)
        {
            return connectionBuilder.Use(next =>
            {
                return context =>
                {
                    return middleware(context);
                };
            });
        }
    }
}