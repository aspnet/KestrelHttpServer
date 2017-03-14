// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class SocketOutput : ISocketOutput
    {
        private static readonly ArraySegment<byte> _emptyData = new ArraySegment<byte>(new byte[0]);

        private readonly KestrelThread _thread;
        private readonly UvStreamHandle _socket;
        private readonly Connection _connection;
        private readonly string _connectionId;
        private readonly IKestrelTrace _log;
        private readonly IThreadPool _threadPool;
        
        // This locks access to to all of the below fields
        private readonly object _contextLock = new object();
        

        private bool _cancelled = false;
        private Exception _lastWriteError;
        private readonly WriteReqPool _writeReqPool;
        private readonly IPipe _pipe;
        private Task _writingTask;

        public SocketOutput(
            IPipe pipe,
            KestrelThread thread,
            UvStreamHandle socket,
            Connection connection,
            string connectionId,
            IKestrelTrace log,
            IThreadPool threadPool)
        {
            _pipe = pipe;
            // We need to have empty pipe at this moment so callback
            // get's scheduled
            _writingTask = StartWrites();
            _thread = thread;
            _socket = socket;
            _connection = connection;
            _connectionId = connectionId;
            _log = log;
            _threadPool = threadPool;
            _writeReqPool = thread.WriteReqPool;
        }

        public async Task WriteAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken,
            bool chunk = false,
            bool socketShutdownSend = false,
            bool socketDisconnect = false,
            bool isSync = false)
        {
            WritableBufferAwaitable? flushAwaiter = null;

            lock (_contextLock)
            {
                if (_socket.IsClosed)
                {
                    _log.ConnectionDisconnectedWrite(_connectionId, buffer.Count, _lastWriteError);

                    return;
                }

                if (buffer.Count > 0)
                {
                    var tail = _pipe.Writer.Alloc();
                    
                    if (chunk)
                    {
                        ChunkWriter.WriteBeginChunkBytes(ref tail, buffer.Count);
                    }

                    tail.Write(buffer);

                    if (chunk)
                    {
                        ChunkWriter.WriteEndChunkBytes(ref tail);
                    }

                    flushAwaiter = tail.FlushAsync();
                }

                if (socketShutdownSend)
                {
                    SocketShutdownSend = true;
                }
                if (socketDisconnect)
                {
                    SocketDisconnect = true;
                }

                if (socketDisconnect || socketShutdownSend)
                {
                    _pipe.Writer.Complete();
                }
            }
            if (flushAwaiter != null)
            {
                await flushAwaiter.Value;
            }
        }

        public bool SocketDisconnect { get; set; }

        public bool SocketShutdownSend { get; set; }

        public void End(ProduceEndType endType)
        {
#pragma warning disable 4014
            switch (endType)
            {
                case ProduceEndType.SocketShutdown:
                    WriteAsync(default(ArraySegment<byte>),
                        default(CancellationToken),
                        socketShutdownSend: true,
                        socketDisconnect: true,
                        isSync: true);
                    break;
                case ProduceEndType.SocketDisconnect:
                    WriteAsync(default(ArraySegment<byte>),
                        default(CancellationToken),
                        socketShutdownSend: false,
                        socketDisconnect: true,
                        isSync: true);
                    break;
            }
#pragma warning restore 4014
        }

        private void CancellationTriggered()
        {
            lock (_contextLock)
            {
                if (!_cancelled)
                {
                    // Abort the connection for any failed write
                    // Queued on threadpool so get it in as first op.
                    _connection.AbortAsync();
                    _cancelled = true;

                    _log.ConnectionError(_connectionId, new TaskCanceledException("Write operation canceled. Aborting connection."));
                }
            }
        }

        // This may called on the libuv event loop
        private void OnWriteCompleted(int writeStatus, Exception writeError)
        {
            // Called inside _contextLock
            var status = writeStatus;
            var error = writeError;

            if (error != null)
            {
                // Abort the connection for any failed write
                // Queued on threadpool so get it in as first op.
                _connection.AbortAsync();
                _cancelled = true;
                _lastWriteError = error;
            }

            if (error == null)
            {
                _log.ConnectionWriteCallback(_connectionId, status);
            }
            else
            {
                // Log connection resets at a lower (Debug) level.
                if (status == Constants.ECONNRESET)
                {
                    _log.ConnectionReset(_connectionId);
                }
                else
                {
                    _log.ConnectionError(_connectionId, error);
                }
            }
        }

        void ISocketOutput.Write(ArraySegment<byte> buffer, bool chunk)
        {
            WriteAsync(buffer, default(CancellationToken), chunk, isSync: true).GetAwaiter().GetResult();
        }

        Task ISocketOutput.WriteAsync(ArraySegment<byte> buffer, bool chunk, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _connection.AbortAsync();
                _cancelled = true;
                return TaskUtilities.GetCancelledTask(cancellationToken);
            }
            else if (_cancelled)
            {
                return TaskCache.CompletedTask;
            }

            return WriteAsync(buffer, cancellationToken, chunk);
        }

        void ISocketOutput.Flush()
        {
            WriteAsync(_emptyData, default(CancellationToken), isSync: true).GetAwaiter().GetResult();
        }

        Task ISocketOutput.FlushAsync(CancellationToken cancellationToken)
        {
            return WriteAsync(_emptyData, cancellationToken);
        }

        public WritableBuffer Alloc()
        {
            return _pipe.Writer.Alloc(1);
        }

        public void Complete()
        {
            _pipe.Writer.Complete();
        }
        
        public async Task StartWrites()
        {
            while (true)
            {
                var result = await _pipe.Reader.ReadAsync();
                try
                {
                    if (result.IsCompleted)
                    {
                        break;
                    }

                    var writeReq = _writeReqPool.Allocate();
                    var writeResult = await writeReq.Write(_socket, result.Buffer);
                    _writeReqPool.Return(writeReq);
                    DoShutdownIfNeeded(writeResult.Status, writeResult.Error);
                }
                finally
                {
                    _pipe.Reader.Advance(result.Buffer.End);
                }
            }

            DoShutdownIfNeeded(0, null);
            _pipe.Reader.Complete();
        }

        public void DoShutdownIfNeeded(int writeStatus, Exception writeError)
        {
            if (SocketShutdownSend == false || _socket.IsClosed)
            {
                DoDisconnectIfNeeded(writeStatus, writeError);
                return;
            }

            _log.ConnectionWriteFin(_connectionId);

            var shutdownReq = new UvShutdownReq(_log);
            shutdownReq.Init(_thread.Loop);
            shutdownReq.Shutdown(_socket, (req, status, state) =>
            {
                req.Dispose();
                _log.ConnectionWroteFin(_connectionId, status);
                DoDisconnectIfNeeded(writeStatus, writeError);
            }, this);
        }

        /// <summary>
        /// Third step: disconnect socket if needed, otherwise this work item is complete
        /// </summary>
        private void DoDisconnectIfNeeded(int writeStatus, Exception writeError)
        {
            if (SocketDisconnect == false || _socket.IsClosed)
            {
                CompleteWithContextLock(writeStatus, writeError);
                return;
            }

            // Ensure all blocks are returned before calling OnSocketClosed
            // to ensure the MemoryPool doesn't get disposed too soon.
            _pipe.Writer.Complete();
            _socket.Dispose();
            _connection.OnSocketClosed();
            _log.ConnectionStop(_connectionId);
            CompleteWithContextLock(writeStatus, writeError);
        }

        private void CompleteWithContextLock(int writeStatus, Exception writeError)
        {
            if (Monitor.TryEnter(_contextLock))
            {
                try
                {
                    OnWriteCompleted(writeStatus, writeError);
                }
                finally
                {
                    Monitor.Exit(_contextLock);
                }
            }
            else
            {
                _threadPool.UnsafeRun((state)=> CompleteOnThreadPool(writeStatus, writeError), this);
            }
        }

        private void CompleteOnThreadPool(int writeStatus, Exception writeError)
        {
            lock (_contextLock)
            {
                try
                {
                    OnWriteCompleted(writeStatus, writeError);
                }
                catch (Exception ex)
                {
                    _log.LogError(0, ex, "SocketOutput.OnWriteCompleted");
                }
            }
        }
    }
}
