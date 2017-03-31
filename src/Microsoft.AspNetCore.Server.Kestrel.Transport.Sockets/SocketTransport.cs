// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.IO.Pipelines;
using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    public class SocketTransport : ITransport
    {
        ListenOptions _listenOptions;
        IConnectionHandler _handler;
        PipeFactory _pipeFactory;
        Socket _listenSocket;
        Task _listenTask;

        public SocketTransport(ListenOptions listenOptions, IConnectionHandler handler)
        {
            _listenOptions = listenOptions;
            _handler = handler;

            // TODO: Maybe should live on SocketTransportFactory?
            // I think I only need one of these for the entire process.
            _pipeFactory = new PipeFactory();

            _listenSocket = null;
            _listenTask = null;
        }

        public ListenOptions ListenOptions => _listenOptions;

        public PipeFactory PipeFactory => _pipeFactory;

        public Task BindAsync()
        {
            if (_listenSocket != null)
            {
                throw new InvalidOperationException();
            }

            if (_listenOptions.Type != ListenType.IPEndPoint)
            {
                throw new InvalidOperationException();
            }

            IPEndPoint endPoint = _listenOptions.IPEndPoint;

            var listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(endPoint);

            // If requested port was "0", replace with assigned dynamic port.
            _listenOptions.IPEndPoint = (IPEndPoint)listenSocket.LocalEndPoint;

            listenSocket.Listen(512);

            _listenSocket = listenSocket;

            _listenTask = Task.Run(() => AcceptLoop());

            return Task.CompletedTask;
        }

        public async Task UnbindAsync()
        {
            if (_listenSocket != null)
            {
                var listenSocket = _listenSocket;
                _listenSocket = null;

                listenSocket.Dispose();

                Debug.Assert(_listenTask != null);
                await _listenTask;
                _listenTask = null;
            }
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        public async Task AcceptLoop()
        {
            try
            {
                while (true)
                {
                    Socket acceptSocket = await _listenSocket.AcceptAsync();

                    SocketConnection connection = new SocketConnection(acceptSocket, this);
                    connection.Start(_handler);
                }
            }
            catch (Exception)
            {
                if (_listenSocket == null)
                {
                    // Means we must be unbinding.  Eat the exception.
                }
                else
                {
                    throw;
                }
            }

        }
    }
}
