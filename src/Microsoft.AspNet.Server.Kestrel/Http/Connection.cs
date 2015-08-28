// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNet.Server.Kestrel.Networking;
using System.Diagnostics;
using Microsoft.Framework.Logging;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class Connection : ConnectionContext, IConnectionControl
    {
        private const int EOF = -4095;
        private const int ECONNRESET = -4077;

        private static readonly Action<UvStreamHandle, int, Exception, object> _readCallback = ReadCallback;
        private static readonly Func<UvStreamHandle, int, object, Libuv.uv_buf_t> _allocCallback = AllocCallback;

        private static Libuv.uv_buf_t AllocCallback(UvStreamHandle handle, int suggestedSize, object state)
        {
            return ((Connection)state).OnAlloc(handle, suggestedSize);
        }

        private static void ReadCallback(UvStreamHandle handle, int nread, Exception error, object state)
        {
            ((Connection)state).OnRead(handle, nread, error);
        }

        private readonly UvStreamHandle _socket;
        private Frame _frame;
        long _connectionId = 0;

        private readonly object _stateLock = new object();
        private ConnectionState _connectionState;

        public Connection(ListenerContext context, UvStreamHandle socket) : base(context)
        {
            _socket = socket;
            ConnectionControl = this;
        }

        public void Start()
        {
            KestrelTrace.Log.ConnectionStart(_connectionId);

            SocketInput = new SocketInput(Memory);
            SocketOutput = new SocketOutput(Thread, _socket);
            _frame = new Frame(this);
            _socket.ReadStart(_allocCallback, _readCallback, this);
        }

        private Libuv.uv_buf_t OnAlloc(UvStreamHandle handle, int suggestedSize)
        {
            return handle.Libuv.buf_init(
                SocketInput.Pin(2048),
                2048);
        }

        private void OnRead(UvStreamHandle handle, int status, Exception error)
        {
            SocketInput.Unpin(status);

            var normalRead = error == null && status > 0;
            var normalDone = status == 0 || status == ECONNRESET || status == EOF;
            var errorDone = !(normalDone || normalRead);

            if (normalRead)
            {
                KestrelTrace.Log.ConnectionRead(_connectionId, status);
            }
            else if (normalDone || errorDone)
            {
                KestrelTrace.Log.ConnectionReadFin(_connectionId);
                SocketInput.RemoteIntakeFin = true;
                _socket.ReadStop();

                if (errorDone && error != null)
                {
                    KestrelTrace.Log.LogError("Connection.OnRead " + error, error);
                }
            }
            
            try
            {
                _frame.Consume();
            }
            catch (Exception ex)
            {
                KestrelTrace.Log.LogError("Connection._frame.Consume " + ex, ex);
            }
        }

        void IConnectionControl.Pause()
        {
            KestrelTrace.Log.ConnectionPause(_connectionId);
            _socket.ReadStop();
        }

        void IConnectionControl.Resume()
        {
            KestrelTrace.Log.ConnectionResume(_connectionId);
            _socket.ReadStart(_allocCallback, _readCallback, this);
        }

        void IConnectionControl.End(ProduceEndType endType)
        {
            lock (_stateLock)
            {
                switch (endType)
                {
                    case ProduceEndType.SocketShutdownSend:
                        if (_connectionState != ConnectionState.Open)
                        {
                            return;
                        }
                        _connectionState = ConnectionState.Shutdown;

                        KestrelTrace.Log.ConnectionWriteFin(_connectionId, 0);
                        Thread.Post(
                            x =>
                            {
                                KestrelTrace.Log.ConnectionWriteFin(_connectionId, 1);
                                var self = (Connection)x;
                                var shutdown = new UvShutdownReq();
                                shutdown.Init(self.Thread.Loop);
                                shutdown.Shutdown(self._socket, (req, status, state) =>
                                {
                                    KestrelTrace.Log.ConnectionWriteFin(_connectionId, 1);
                                    req.Dispose();
                                }, null);
                            },
                            this);
                        break;
                    case ProduceEndType.ConnectionKeepAlive:
                        if (_connectionState != ConnectionState.Open)
                        {
                            return;
                        }

                        KestrelTrace.Log.ConnectionKeepAlive(_connectionId);
                        _frame = new Frame(this);
                        Thread.Post(
                            x => ((Frame)x).Consume(),
                            _frame);
                        break;
                    case ProduceEndType.SocketDisconnect:
                        if (_connectionState == ConnectionState.Disconnected)
                        {
                            return;
                        }
                        _connectionState = ConnectionState.Disconnected;

                        KestrelTrace.Log.ConnectionDisconnect(_connectionId);
                        Thread.Post(
                            x =>
                            {
                                KestrelTrace.Log.ConnectionStop(_connectionId);
                                ((UvHandle)x).Dispose();
                            },
                            _socket);
                        break;
                }
            }
        }

        private enum ConnectionState
        {
            Open,
            Shutdown,
            Disconnected
        }
    }
}
