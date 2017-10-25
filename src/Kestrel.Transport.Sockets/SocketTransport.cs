﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    internal sealed class SocketTransport : ITransport
    {
        private readonly PipeFactory _pipeFactory = new PipeFactory();
        private readonly IEndPointInformation _endPointInformation;
        private readonly IConnectionHandler _handler;
        private readonly IApplicationLifetime _appLifetime;
        private readonly ISocketsTrace _trace;
        private Socket _listenSocket;
        private Task _listenTask;
        private Exception _listenException;
        private volatile bool _unbinding;

        internal SocketTransport(
            IEndPointInformation endPointInformation,
            IConnectionHandler handler,
            IApplicationLifetime applicationLifetime,
            ISocketsTrace trace)
        {
            Debug.Assert(endPointInformation != null);
            Debug.Assert(endPointInformation.Type == ListenType.IPEndPoint);
            Debug.Assert(handler != null);
            Debug.Assert(applicationLifetime != null);
            Debug.Assert(trace != null);

            _endPointInformation = endPointInformation;
            _handler = handler;
            _appLifetime = applicationLifetime;
            _trace = trace;
        }

        public Task BindAsync()
        {
            if (_listenSocket != null)
            {
                throw new InvalidOperationException(SocketsStrings.TransportAlreadyBound);
            }

            IPEndPoint endPoint = _endPointInformation.IPEndPoint;

            var listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            EnableRebinding(listenSocket);

            // Kestrel expects IPv6Any to bind to both IPv6 and IPv4
            if (endPoint.Address == IPAddress.IPv6Any)
            {
                listenSocket.DualMode = true;
            }

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
                _endPointInformation.IPEndPoint = (IPEndPoint)listenSocket.LocalEndPoint;
            }

            listenSocket.Listen(512);

            _listenSocket = listenSocket;

            _listenTask = Task.Run(() => RunAcceptLoopAsync());

            return Task.CompletedTask;
        }

        public async Task UnbindAsync()
        {
            if (_listenSocket != null)
            {
                _unbinding = true;
                _listenSocket.Dispose();

                Debug.Assert(_listenTask != null);
                await _listenTask.ConfigureAwait(false);

                _unbinding = false;
                _listenSocket = null;
                _listenTask = null;

                if (_listenException != null)
                {
                    var exInfo = ExceptionDispatchInfo.Capture(_listenException);
                    _listenException = null;
                    exInfo.Throw();
                }
            }
        }

        public Task StopAsync()
        {
            _pipeFactory.Dispose();
            return Task.CompletedTask;
        }

        private async Task RunAcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    try
                    {
                        var acceptSocket = await _listenSocket.AcceptAsync();
                        acceptSocket.NoDelay = _endPointInformation.NoDelay;

                        var connection = new SocketConnection(acceptSocket, _pipeFactory, _trace);
                        _ = connection.StartAsync(_handler);
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        // REVIEW: Should there be a seperate log message for a connection reset this early?
                        _trace.ConnectionReset(connectionId: "(null)");
                    }
                    catch (SocketException ex) when (!_unbinding)
                    {
                        _trace.ConnectionError(connectionId: "(null)", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_unbinding)
                {
                    // Means we must be unbinding. Eat the exception.
                }
                else
                {
                    _trace.LogCritical($"Unexpected exeption in {nameof(SocketTransport)}.{nameof(RunAcceptLoopAsync)}.");
                    _listenException = ex;
                    
                    // Request shutdown so we can rethrow this exception
                    // in Stop which should be observable.
                    _appLifetime.StopApplication();
                }
            }
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int setsockopt(IntPtr socket, int level, int option_name, IntPtr option_value, uint option_len);

        private const int SOL_SOCKET_OSX = 0xffff;
        private const int SO_REUSEADDR_OSX = 0x0004;
        private const int SOL_SOCKET_LINUX = 0x0001;
        private const int SO_REUSEADDR_LINUX = 0x0002;

        // Without setting SO_REUSEADDR on macOS and Linux, binding to a recently used endpoint can fail.
        // https://github.com/dotnet/corefx/issues/24562
        private unsafe void EnableRebinding(Socket listenSocket)
        {
            var optionValue = 1;
            var setsockoptStatus = 0;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                setsockoptStatus = setsockopt(listenSocket.Handle, SOL_SOCKET_LINUX, SO_REUSEADDR_LINUX,
                                              (IntPtr)(&optionValue), sizeof(int));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                setsockoptStatus = setsockopt(listenSocket.Handle, SOL_SOCKET_OSX, SO_REUSEADDR_OSX,
                                              (IntPtr)(&optionValue), sizeof(int));
            }

            if (setsockoptStatus != 0)
            {
                _trace.LogInformation("Setting SO_REUSEADDR failed with errno '{errno}'.", Marshal.GetLastWin32Error());
            }
        }
    }
}
