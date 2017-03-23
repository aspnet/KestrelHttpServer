// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;
using Microsoft.Extensions.Internal;

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

        // This locks access to to all of the below fields
        private readonly object _contextLock = new object();

        private bool _cancelled = false;
        private bool _completed = false;
        private Exception _lastWriteError;
        private readonly WriteReqPool _writeReqPool;
        private readonly IPipe _pipe;
        private Task _writingTask;

        private readonly WritableBufferFlusher _flusher = new WritableBufferFlusher();

        public SocketOutput(
            IPipe pipe,
            KestrelThread thread,
            UvStreamHandle socket,
            Connection connection,
            string connectionId,
            IKestrelTrace log)
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
            _writeReqPool = thread.WriteReqPool;
        }

        public Task WriteAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken,
            bool chunk = false)
        {
            var writableBuffer = default(WritableBuffer);

            lock (_contextLock)
            {
                if (_socket.IsClosed)
                {
                    _log.ConnectionDisconnectedWrite(_connectionId, buffer.Count, _lastWriteError);

                    return TaskCache.CompletedTask;
                }

                if (_completed)
                {
                    return TaskCache.CompletedTask;
                }

                writableBuffer = _pipe.Writer.Alloc();

                if (buffer.Count > 0)
                {
                    var writer = writableBuffer.CreateWriter();

                    if (chunk)
                    {
                        ChunkWriter.WriteBeginChunkBytes(ref writer, buffer.Count);
                        writer.WriteFast(buffer);
                        ChunkWriter.WriteEndChunkBytes(ref writer);
                    }
                    else
                    {
                        writer.WriteFast(buffer);
                    }

                    writer.Commit();
                }

                writableBuffer.Commit();
            }

            return _flusher.FlushAsync(writableBuffer);
        }

        public void End(ProduceEndType endType)
        {
            if (endType == ProduceEndType.SocketShutdown)
            {
                // Graceful shutdown
                _pipe.Reader.CancelPendingRead();
            }

            lock (_contextLock)
            {
                _completed = true;
            }

            // We're done writing
            _pipe.Writer.Complete();
        }

        void ISocketOutput.Write(ArraySegment<byte> buffer, bool chunk)
        {
            WriteAsync(buffer, default(CancellationToken), chunk).GetAwaiter().GetResult();
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
            WriteAsync(_emptyData, default(CancellationToken)).GetAwaiter().GetResult();
        }

        Task ISocketOutput.FlushAsync(CancellationToken cancellationToken)
        {
            return WriteAsync(_emptyData, cancellationToken);
        }

        public void Write<T>(Action<WritableBuffer, T> callback, T state)
        {
            lock (_contextLock)
            {
                if (_completed)
                {
                    return;
                }

                var buffer = _pipe.Writer.Alloc(1);
                callback(buffer, state);
                buffer.Commit();
            }
        }

        public async Task StartWrites()
        {
            while (true)
            {
                var result = await _pipe.Reader.ReadAsync();
                var buffer = result.Buffer;

                try
                {
                    if (!buffer.IsEmpty)
                    {
                        var writeReq = _writeReqPool.Allocate();
                        var writeResult = await writeReq.WriteAsync(_socket, buffer);
                        _writeReqPool.Return(writeReq);

                        // REVIEW: Locking here, do we need to take the context lock?
                        OnWriteCompleted(writeResult.Status, writeResult.Error);
                    }

                    if (result.IsCancelled)
                    {
                        // Send a FIN
                        await ShutdownAsync();
                    }

                    if (buffer.IsEmpty && result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    _pipe.Reader.Advance(result.Buffer.End);
                }
            }

            // We're done reading
            _pipe.Reader.Complete();

            _socket.Dispose();
            _connection.OnSocketClosed();
            _log.ConnectionStop(_connectionId);
        }

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

        private Task ShutdownAsync()
        {
            var tcs = new TaskCompletionSource<object>();
            _log.ConnectionWriteFin(_connectionId);

            var shutdownReq = new UvShutdownReq(_log);
            shutdownReq.Init(_thread.Loop);
            shutdownReq.Shutdown(_socket, (req, status, state) =>
            {
                req.Dispose();
                _log.ConnectionWroteFin(_connectionId, status);

                tcs.TrySetResult(null);
            },
            this);

            return tcs.Task;
        }
    }
}
