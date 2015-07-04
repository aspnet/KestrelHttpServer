// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Networking;
using System.Diagnostics;
using System.Runtime.InteropServices;

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
        long _connectionId;

        public Connection(ListenerContext context, UvStreamHandle socket) : base(context)
        {
            _socket = socket;
            ConnectionControl = this;
        }

        private void ExtractIP()
        {
            // storage for binary IP addresses
            Libuv.sockaddr peer;
            Libuv.sockaddr local;
            // length of sockaddr structs
            var peerLength = Marshal.SizeOf(typeof(Libuv.sockaddr));
            var localLength = peerLength;
            // buffers for addresses
            var peerString = new StringBuilder(50);
            var localString = new StringBuilder(50);
            // storage for errors during decoding processes
            Exception peerException;
            Exception localException;

            // get local and peer IPs in binary form
            _socket.Libuv.tcp_getsockname((UvTcpHandle)_socket, out local, ref localLength);
            _socket.Libuv.tcp_getpeername((UvTcpHandle)_socket, out peer, ref peerLength);
            // decode as IPv4 addresses
            _socket.Libuv.ip4_name(ref local, localString, 50, out localException);
            _socket.Libuv.ip4_name(ref peer, peerString, 50, out peerException);

            if (localException != null)
            {
                throw localException;
            }

            if (peerException != null)
            {
                throw peerException;
            }

            // put IPs as part of Frame information
            _frame.RemoteIpAddress = peerString.ToString();
            _frame.LocalIpAddress = localString.ToString();
        }

        public void Start()
        {
            KestrelTrace.Log.ConnectionStart(_connectionId);

            SocketInput = new SocketInput(Memory);
            SocketOutput = new SocketOutput(Thread, _socket);

            _frame = new Frame(this);
            ExtractIP();

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
                _socket.ReadStop();

                if (errorDone && error != null)
                {
                    Trace.WriteLine("Connection.OnRead " + error.ToString());
                }
            }

            try
            {
                ExtractIP();
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
            _socket.ReadStop();
        }

        void IConnectionControl.Resume()
        {
            KestrelTrace.Log.ConnectionResume(_connectionId);
            _socket.ReadStart(_allocCallback, _readCallback, this);
        }

        void IConnectionControl.End(ProduceEndType endType)
        {
            switch (endType)
            {
                case ProduceEndType.SocketShutdownSend:
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
                    KestrelTrace.Log.ConnectionKeepAlive(_connectionId);
                    _frame = new Frame(this);
                    ExtractIP();
                    Thread.Post(
                        x => ((Frame)x).Consume(),
                        _frame);
                    break;
                case ProduceEndType.SocketDisconnect:
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
}
