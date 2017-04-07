// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    public class SocketTransport : ITransport
    {
        IEndPointInformation _endPointInformation;
        IConnectionHandler _handler;
        PipeFactory _pipeFactory;
        Socket _listenSocket;
        Task _listenTask;

        public SocketTransport(IEndPointInformation endPointInformation, IConnectionHandler handler)
        {
            if (endPointInformation == null)
            {
                throw new ArgumentNullException(nameof(endPointInformation));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _endPointInformation = endPointInformation;
            _handler = handler;

            // TODO: Maybe should live on SocketTransportFactory?
            // I think I only need one of these for the entire process.
            _pipeFactory = new PipeFactory();

            _listenSocket = null;
            _listenTask = null;
        }

        public PipeFactory PipeFactory => _pipeFactory;

        public Task BindAsync()
        {
            if (_listenSocket != null)
            {
                throw new InvalidOperationException();
            }

            if (_endPointInformation.Type != ListenType.IPEndPoint)
            {
                throw new InvalidOperationException();
            }

            IPEndPoint endPoint = _endPointInformation.IPEndPoint;

            var listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // Kestrel expects IPv6Any to bind to both IPv6 and IPv4
            if (endPoint.Address == IPAddress.IPv6Any)
            {
                listenSocket.DualMode = true;
            }

            try
            {
                listenSocket.Bind(endPoint);
            }
            catch (SocketException e)
            {
                // Convert to an IO exception, since this is what tests expect
                // Note the tests actually validate the exact message, which seems questionable
                // (Actually I just disabled that check for now.  Revisit later.)
                throw new IOException($"Failed to bind to address http://{endPoint}: address already in use.", e);
            }

            // If requested port was "0", replace with assigned dynamic port.
            if (_endPointInformation.IPEndPoint.Port == 0)
            {
                _endPointInformation.IPEndPoint = (IPEndPoint)listenSocket.LocalEndPoint;
            }

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

                    acceptSocket.NoDelay = _endPointInformation.NoDelay;

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
