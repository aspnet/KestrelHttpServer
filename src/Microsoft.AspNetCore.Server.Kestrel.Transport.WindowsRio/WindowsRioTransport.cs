// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio
{
    internal sealed class WindowsRioTransport : ITransport
    {
        private readonly WindowsRioTransportFactory _transportFactory;
        private readonly IEndPointInformation _endPointInformation;
        private readonly IConnectionHandler _handler;
        private RioListenSocket _listenSocket;
        private Task _listenTask;

        public BufferMapper BufferMapper => _transportFactory.BufferMapper;

        internal WindowsRioTransport(WindowsRioTransportFactory transportFactory, IEndPointInformation endPointInformation, IConnectionHandler handler)
        {
            Debug.Assert(transportFactory != null);
            Debug.Assert(endPointInformation != null);
            Debug.Assert(endPointInformation.Type == ListenType.IPEndPoint);
            Debug.Assert(handler != null);

            _transportFactory = transportFactory;
            _endPointInformation = endPointInformation;
            _handler = handler;

            _listenTask = null;
        }

        public Task BindAsync()
        {
            if (!_listenSocket.IsNull)
            {
                throw new InvalidOperationException("Transport is already bound");
            }

            IPEndPoint endPoint = _endPointInformation.IPEndPoint;

            var listenSocket = RioListenSocket.Create();

            try
            {
                listenSocket.Bind(endPoint);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                throw new AddressInUseException(e.Message, e);
            }

            // If requested port was "0", replace with assigned dynamic port.
            if (_endPointInformation.IPEndPoint.Port == 0)
            {
                _endPointInformation.IPEndPoint = listenSocket.LocalEndPoint;
            }

            listenSocket.Listen(512);

            _listenSocket = listenSocket;

            _listenTask = Task.Run(() => RunAcceptLoopAsync());

            return Task.CompletedTask;
        }

        public async Task UnbindAsync()
        {
            if (_listenSocket.IsNull)
            {
                _listenSocket.Dispose();

                Debug.Assert(_listenTask != null);
                await _listenTask.ConfigureAwait(false);
                _listenTask = null;
            }
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        private Task RunAcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    var connectedSocket = _listenSocket.AcceptSocket(); //.AcceptAsync();

                    var socket = new RioSocket(connectedSocket, BufferMapper);
                    socket.NoDelay = _endPointInformation.NoDelay;

                    var connection = new WindowsRioConnection(socket, this);
                    connection.Start(_handler);
                }
            }
            catch (Exception)
            {
                if (_listenSocket.IsNull)
                {
                    // Means we must be unbinding.  Eat the exception.
                }
                else
                {
                    throw;
                }
            }

            return Task.CompletedTask;
        }

        internal WindowsRioTransportFactory TransportFactory => _transportFactory;
    }
}
