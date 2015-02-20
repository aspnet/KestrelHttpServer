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
        void End(ProduceEndType endType);
    }

    public class Connection : ConnectionContext, IConnectionControl
    {
        private readonly Action<int, Exception> _readCallback;
        private readonly Func<int, UvBuffer> _allocCallback;
        private readonly UvTcpStreamHandle _socket;

        private UvReadHandle _read;
        private Frame _frame;
        long _connectionId;

        public Connection(ListenerContext context, UvTcpStreamHandle socket) : base(context)
        {
            _readCallback = OnRead;
            _allocCallback = OnAlloc;
            _socket = socket;
            ConnectionControl = this;

            KestrelTrace.Log.ConnectionStart(_connectionId);

            SocketInput = new SocketInput(Memory);
            SocketOutput = new SocketOutput(Thread, _socket);
            _frame = new Frame(this);
            _read = new UvReadHandle(_socket, _allocCallback, _readCallback);
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
            var errorDone = !(normalDone || normalRead);

            if (normalRead)
            {
                KestrelTrace.Log.ConnectionRead(_connectionId, status);
            }
            else if (normalDone || errorDone)
            {
                KestrelTrace.Log.ConnectionReadFin(_connectionId);
                SocketInput.RemoteIntakeFin = true;
                _read.Dispose();
                _read = null;

                if (errorDone && error != null)
                {
                    Trace.WriteLine("Connection.OnRead " + error.ToString());
                }
            }


            try
            {
                _frame.Consume();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Connection._frame.Consume " + ex.ToString());
            }
        }

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

        void IConnectionControl.End(ProduceEndType endType)
        {
            switch (endType)
            {
                case ProduceEndType.SocketShutdownSend:
                    KestrelTrace.Log.ConnectionWriteFin(_connectionId, 0);
                    Thread.Post(() =>
                    {
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
                    Thread.Post(_frame.Consume);
                    break;
                case ProduceEndType.SocketDisconnect:
                    KestrelTrace.Log.ConnectionDisconnect(_connectionId);
                    Thread.Post(() =>
                    {
                        _read?.Dispose(); // Remove the ? once connections closed by the client work
                        _socket.Dispose();
                        KestrelTrace.Log.ConnectionStop(_connectionId);
                    });
                    break;
            }
        }
    }
}
