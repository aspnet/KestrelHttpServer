// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal
{
    public class LibuvConnection : LibuvConnectionContext
    {
        private const int MinAllocBufferSize = 2048;

        private static readonly Action<UvStreamHandle, int, object> _readCallback =
            (handle, status, state) => ReadCallback(handle, status, state);

        private static readonly Func<UvStreamHandle, int, object, LibuvFunctions.uv_buf_t> _allocCallback =
            (handle, suggestedsize, state) => AllocCallback(handle, suggestedsize, state);

        private readonly UvStreamHandle _socket;
        private IConnectionContext _connectionContext;

        private WritableBuffer? _currentWritableBuffer;
        private BufferHandle _bufferHandle;

        public LibuvConnection(ListenerContext context, UvStreamHandle socket) : base(context)
        {
            _socket = socket;

            if (_socket is UvTcpHandle tcpHandle)
            {
                RemoteEndPoint = tcpHandle.GetPeerIPEndPoint();
                LocalEndPoint = tcpHandle.GetSockIPEndPoint();
            }
        }

        // For testing
        public LibuvConnection()
        {
        }

        public string ConnectionId { get; set; }
        public IPipeWriter Input { get; set; }
        public LibuvOutputConsumer Output { get; set; }

        private ILibuvTrace Log => ListenerContext.TransportContext.Log;
        private IConnectionHandler ConnectionHandler => ListenerContext.TransportContext.ConnectionHandler;
        private LibuvThread Thread => ListenerContext.Thread;

        public async Task Start()
        {
            try
            {
                _connectionContext = ConnectionHandler.OnConnection(this);
                ConnectionId = _connectionContext.ConnectionId;

                Input = _connectionContext.Input;
                Output = new LibuvOutputConsumer(_connectionContext.Output, Thread, _socket, ConnectionId, Log);

                StartReading();

                try
                {
                    // This *must* happen after socket.ReadStart
                    // The socket output consumer is the only thing that can close the connection. If the
                    // output pipe is already closed by the time we start then it's fine since, it'll close gracefully afterwards.
                    await Output.WriteOutputAsync();
                    _connectionContext.Output.Complete();

                    // Now, complete the input so that no more reads can happen
                    Input.Complete(new TaskCanceledException("The request was aborted"));
                }
                catch (UvException ex)
                {
                    var ioEx = new IOException(ex.Message, ex);
                    _connectionContext.Output.Complete(ioEx);
                    Input.Complete(ioEx);
                }
                finally
                {
                    // Make sure it isn't possible for a paused read to resume reading after calling uv_close
                    // on the stream handle
                    Input.CancelPendingFlush();

                    // We're done with the socket now
                    _socket.Dispose();

                    // Tell the kestrel we're done with this connection
                    _connectionContext.OnConnectionClosed();
                }
            }
            catch (Exception e)
            {
                Log.LogCritical(0, e, $"{nameof(LibuvConnection)}.{nameof(Start)}() {ConnectionId}");
            }
        }

        // Called on Libuv thread
        private static LibuvFunctions.uv_buf_t AllocCallback(UvStreamHandle handle, int suggestedSize, object state)
        {
            return ((LibuvConnection)state).OnAlloc(handle, suggestedSize);
        }

        private unsafe LibuvFunctions.uv_buf_t OnAlloc(UvStreamHandle handle, int suggestedSize)
        {
            Debug.Assert(_currentWritableBuffer == null);
            var currentWritableBuffer = Input.Alloc(MinAllocBufferSize);
            _currentWritableBuffer = currentWritableBuffer;

            _bufferHandle = currentWritableBuffer.Buffer.Pin();

            return handle.Libuv.buf_init((IntPtr)_bufferHandle.PinnedPointer, currentWritableBuffer.Buffer.Length);
        }

        private static void ReadCallback(UvStreamHandle handle, int status, object state)
        {
            ((LibuvConnection)state).OnRead(handle, status);
        }

        private void OnRead(UvStreamHandle handle, int status)
        {
            var normalRead = status >= 0;
            var normalDone = status == LibuvConstants.EOF;
            var errorDone = !(normalDone || normalRead);
            var readCount = normalRead ? status : 0;

            if (normalRead)
            {
                Log.ConnectionRead(ConnectionId, readCount);
            }
            else
            {
                _socket.ReadStop();

                if (normalDone)
                {
                    Log.ConnectionReadFin(ConnectionId);
                }
            }

            IOException error = null;
            WritableBufferAwaitable? flushTask = null;
            if (errorDone)
            {
                handle.Libuv.Check(status, out var uvError);

                // Log connection resets at a lower (Debug) level.
                if (status == LibuvConstants.ECONNRESET)
                {
                    Log.ConnectionReset(ConnectionId);
                    error = new ConnectionResetException(uvError.Message, uvError);
                }
                else
                {
                    Log.ConnectionError(ConnectionId, uvError);
                    error = new IOException(uvError.Message, uvError);
                }

                _currentWritableBuffer?.Commit();
            }
            else
            {
                Debug.Assert(_currentWritableBuffer != null);

                var currentWritableBuffer = _currentWritableBuffer.Value;
                currentWritableBuffer.Advance(readCount);
                flushTask = currentWritableBuffer.FlushAsync();
            }

            _currentWritableBuffer = null;
            _bufferHandle.Free();

            if (!normalRead)
            {
                _connectionContext.Abort(error);

                // Complete after aborting the connection
                Input.Complete(error);
            }
            else if (flushTask?.IsCompleted == false)
            {
                // We wrote too many bytes too the reader so pause reading and resume when
                // we hit the low water mark
                _ = ApplyBackpressureAsync(flushTask.Value);
            }
        }

        private async Task ApplyBackpressureAsync(WritableBufferAwaitable flushTask)
        {
            Log.ConnectionPause(ConnectionId);
            StopReading();

            var result = await flushTask;

            // If the reader isn't complete or cancelled then resume reading
            if (!result.IsCompleted && !result.IsCancelled)
            {
                Log.ConnectionResume(ConnectionId);
                StartReading();
            }
        }

        private void StopReading()
        {
            _socket.ReadStop();
        }

        private void StartReading()
        {
            try
            {
                _socket.ReadStart(_allocCallback, _readCallback, this);
            }
            catch (UvException ex)
            {
                // ReadStart() can throw a UvException in some cases (e.g. socket is no longer connected).
                // This should be treated the same as OnRead() seeing a "normalDone" condition.
                Log.ConnectionReadFin(ConnectionId);
                Input.Complete(new IOException(ex.Message, ex));
            }
        }
    }
}
