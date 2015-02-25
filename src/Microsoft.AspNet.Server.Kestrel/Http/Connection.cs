// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Networking;
using System.Diagnostics;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class ConnectionContext : ListenerContext
    {
        public ConnectionContext()
        {
        }

        public ConnectionContext(ListenerContext context) : base(context)
        {
        }

        public ConnectionContext(ConnectionContext context) : base(context)
        {
            SocketInput = context.SocketInput;
            SocketOutput = context.SocketOutput;
            ConnectionControl = context.ConnectionControl;
        }

        public SocketInput SocketInput { get; set; }
        public ISocketOutput SocketOutput { get; set; }

        public IConnectionControl ConnectionControl { get; set; }
    }

    public interface IConnectionControl
    {
        void Pause();
        void Resume();
        Task EndAsync(ProduceEndType endType);
        bool IsInKeepAlive { get; }
    }

    public class Connection : ConnectionContext, IConnectionControl
    {
        private readonly Action<int, Exception> _readCallback;
        private readonly Func<int, UvBuffer> _allocCallback;
        private readonly UvTcpStreamHandle _socket;
        private readonly Listener _listener;

        private UvReadHandle _read;
        private Frame _frame;
        long _connectionId;
        private bool _isInKeepAlive;

        public Connection(Listener listener, UvTcpStreamHandle socket) : base(listener)
        {
            _readCallback = OnRead;
            _allocCallback = OnAlloc;
            _socket = socket;
            ConnectionControl = this;
            _listener = listener;

            KestrelTrace.Log.ConnectionStart(_connectionId);

            SocketInput = new SocketInput(Memory);
            SocketOutput = new SocketOutput(Thread, _socket);
            _frame = new Frame(this);
            _read = new UvReadHandle(_socket, _allocCallback, _readCallback);
            listener.AddConnection(this);
        }

        private UvBuffer OnAlloc(int suggestedSize)
        {
            const int bufferSize = 2048;
            return new UvBuffer(SocketInput.Pin(bufferSize), bufferSize);
        }

        private void OnRead(int status, Exception error)
        {
            SocketInput.Unpin(status);

            var normalRead = error == null && status > 0;
            var normalDone = status == 0 || status == -4077 || status == -4095;

            if (!normalRead)
            {
                KestrelTrace.Log.ConnectionReadFin(_connectionId);
                SocketInput.RemoteIntakeFin = true;
                if (status != -4095)
                {
                    _read.Dispose();
                    _read = null;
                    _listener.RemoveConnection(this);
                    _socket.Dispose();

                    // Not sure if this is right
                    // It should be, but there are some interesting code paths
                    // while reading the message body regarding status == 0 && RemoteIntakeFin
                    return;
                }

                if (!normalDone && error != null)
                {
                    Trace.WriteLine("Connection.OnRead " + error.ToString());
                }
            }

            KestrelTrace.Log.ConnectionRead(_connectionId, status);

            try
            {
                _isInKeepAlive = false;
                _frame.Consume();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Connection._frame.Consume " + ex.ToString());
            }
        }

        bool IConnectionControl.IsInKeepAlive => _isInKeepAlive;

        void IConnectionControl.Pause()
        {
            KestrelTrace.Log.ConnectionPause(_connectionId);
            Debug.Assert(_read != null);
            _read.Dispose();
            _read = null;
        }

        void IConnectionControl.Resume()
        {
            KestrelTrace.Log.ConnectionResume(_connectionId);
            Debug.Assert(_read == null);
            _read = new UvReadHandle(_socket, _allocCallback, _readCallback);
        }

        async Task IConnectionControl.EndAsync(ProduceEndType endType)
        {
            switch (endType)
            {
                case ProduceEndType.SocketShutdownSend:
                    KestrelTrace.Log.ConnectionWriteFin(_connectionId, 0);
                    await Thread.PostAsync(() =>
                    {
                        if (_read == null)
                            return;

                        KestrelTrace.Log.ConnectionWriteFin(_connectionId, 1);
                        new UvShutdownReq(
                            Thread.Loop,
                            _socket,
                            (req, status) =>
                            {
                                KestrelTrace.Log.ConnectionWriteFin(_connectionId, 1);
                                // This connection is now done
                            });
                    });
                    break;
                case ProduceEndType.ConnectionKeepAlive:
                    KestrelTrace.Log.ConnectionKeepAlive(_connectionId);
                    _frame = new Frame(this);
                    _isInKeepAlive = true;
                    await Thread.PostAsync(_frame.Consume);
                    break;
                case ProduceEndType.SocketDisconnect:
                    KestrelTrace.Log.ConnectionDisconnect(_connectionId);
                    await Thread.PostAsync(() =>
                    {
                        _listener.RemoveConnection(this);
                        if (_read == null)
                            return;

                        _read.Dispose();
                        _socket.Dispose();
                        KestrelTrace.Log.ConnectionStop(_connectionId);
                    });
                    break;
                default:
                    throw new ArgumentException(nameof(endType));
            }
        }
    }
}
