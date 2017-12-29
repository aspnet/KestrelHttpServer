// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    public sealed class SocketTransportFactory : ITransportFactory
    {
        private readonly SocketsTrace _trace;
        private readonly IApplicationLifetime _appLifetime;

        public SocketTransportFactory(
            IOptions<SocketTransportOptions> options,
            IApplicationLifetime applicationLifetime,
            ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (applicationLifetime == null)
            {
                throw new ArgumentNullException(nameof(applicationLifetime));
            }
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _appLifetime = applicationLifetime;
            var logger  = loggerFactory.CreateLogger("Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets");
            _trace = new SocketsTrace(logger);
        }

        public ITransport Create(IEndPointInformation endPointInformation, IConnectionHandler handler)
        {
            if (endPointInformation == null)
            {
                throw new ArgumentNullException(nameof(endPointInformation));
            }

            if (endPointInformation.Type != ListenType.IPEndPoint)
            {
                throw new ArgumentException(SocketsStrings.OnlyIPEndPointsSupported, nameof(endPointInformation));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return new SocketTransport(endPointInformation, handler, _appLifetime, _trace);
        }

        public static Task<IPipeConnection> ConnectAsync(EndPoint endpoint, MemoryPool memoryPool)
        {
            var args = new SocketAsyncEventArgs();
            args.RemoteEndPoint = endpoint;
            args.Completed += (sender, p) => OnConnect(p);
            var tcs = new TaskCompletionSource<IPipeConnection>(memoryPool);
            args.UserToken = tcs;
            if (!Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, args))
            {
                OnConnect(args); // completed sync - usually means failure
            }
            return tcs.Task;
        }

        private static void OnConnect(SocketAsyncEventArgs e)
        {
            var tcs = (TaskCompletionSource<IPipeConnection>)e.UserToken;
            try
            {
                if (e.SocketError == SocketError.Success)
                {
                    e.ConnectSocket.NoDelay = true;
                    var connection = new SocketConnection(e.ConnectSocket, (MemoryPool)tcs.Task.AsyncState, SocketsTrace.Nil);

                    //var options = new PipeOptions(connection.MemoryPool);

                    //var receivePipe = new Pipe(options);
                    //var sendPipe = new Pipe(options);

                    //var pipe = new PipeConnection(receivePipe.Reader, sendPipe.Writer);
                    var pair = PipeFactory.CreateConnectionPair(connection.MemoryPool);
                    connection.Transport = pair.Transport;
                    connection.Application = pair.Application;
                    _ = connection.StartAsync(null);
                    tcs.TrySetResult(connection.Transport);
                }
                else
                {
                    tcs.TrySetException(new SocketException((int)e.SocketError));
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }
    }
}
