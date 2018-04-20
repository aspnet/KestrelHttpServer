// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public class OutputProducer : IDisposable
    {
        private static readonly ArraySegment<byte> _emptyData = new ArraySegment<byte>(new byte[0]);

        private readonly string _connectionId;
        private readonly ITimeoutControl _timeoutControl;
        private readonly IKestrelTrace _log;
        private readonly IConnectionInformationExtended _transportConnection;

        // This locks access to to all of the below fields
        private readonly object _contextLock = new object();

        private bool _completed = false;
        private bool _aborted;
        private long _unflushedBytes;
        private long _totalBytesCommitted;

        private readonly IPipe _pipe;

        // https://github.com/dotnet/corefxlab/issues/1334
        // Pipelines don't support multiple awaiters on flush
        // this is temporary until it does
        private TaskCompletionSource<object> _flushTcs;
        private readonly object _flushLock = new object();
        private Action _flushCompleted;

        public OutputProducer(IPipe pipe, string connectionId, IKestrelTrace log, ITimeoutControl timeoutControl)
            : this(pipe, connectionId, log, timeoutControl, null)
        {
        }

        public OutputProducer(
            IPipe pipe,
            string connectionId,
            IKestrelTrace log,
            ITimeoutControl timeoutControl,
            IConnectionInformationExtended connection)
        {
            _pipe = pipe;
            _connectionId = connectionId;
            _timeoutControl = timeoutControl;
            _transportConnection = connection;
            _log = log;
            _flushCompleted = OnFlushCompleted;
        }

        public Task WriteAsync(ArraySegment<byte> buffer, bool chunk = false, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            return WriteAsync(buffer, cancellationToken, chunk);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
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

                var bytesWritten = buffer.BytesWritten;
                _unflushedBytes += bytesWritten;
                _totalBytesCommitted += bytesWritten;

                buffer.Commit();
            }
        }

        public void Dispose()
        {
            lock (_contextLock)
            {
                if (_completed)
                {
                    return;
                }

                _log.ConnectionDisconnect(_connectionId);
                _completed = true;
                _pipe.Writer.Complete();

                if (_transportConnection != null)
                {
                    var unflushedBytes = _totalBytesCommitted - _transportConnection.TotalBytesWritten;

                    if (unflushedBytes > 0)
                    {
                        _timeoutControl.StartTimingWrite(unflushedBytes);
                        _pipe.Writer.OnReaderCompleted((ex, state) => ((ITimeoutControl)state).StopTimingWrite(), _timeoutControl);
                    }
                }
            }
        }

        public void Abort(Exception error)
        {
            // Abort can be called after Dispose if there's a flush timeout.
            // It's important to still call _transportConnection.Abort() in this case.
            lock (_contextLock)
            {
                if (_aborted)
                {
                    return;
                }

                if (!_completed)
                {
                    _log.ConnectionDisconnect(_connectionId);
                    _completed = true;

                    _pipe.Reader.CancelPendingRead();
                    _pipe.Writer.Complete(error);
                }

                _aborted = true;
                _transportConnection?.Abort(error);
            }
        }

        private Task WriteAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken,
            bool chunk = false)
        {
            var writableBuffer = default(WritableBuffer);
            long unflushedBytes;

            lock (_contextLock)
            {
                if (_completed)
                {
                    return Task.CompletedTask;
                }

                writableBuffer = _pipe.Writer.Alloc(1);
                var writer = new WritableBufferWriter(writableBuffer);
                if (buffer.Count > 0)
                {
                    if (chunk)
                    {
                        ChunkWriter.WriteBeginChunkBytes(ref writer, buffer.Count);
                    }

                    writer.Write(buffer.Array, buffer.Offset, buffer.Count);

                    if (chunk)
                    {
                        ChunkWriter.WriteEndChunkBytes(ref writer);
                    }
                }

                var bytesWritten = writableBuffer.BytesWritten;
                _unflushedBytes += bytesWritten;
                _totalBytesCommitted += bytesWritten;

                writableBuffer.Commit();

                unflushedBytes = _unflushedBytes;
                _unflushedBytes = 0;
            }

            return FlushAsync(writableBuffer, unflushedBytes, cancellationToken);
        }

        private Task FlushAsync(WritableBuffer writableBuffer,
            long count,
            CancellationToken cancellationToken)
        {
            var awaitable = writableBuffer.FlushAsync(cancellationToken);
            if (awaitable.IsCompleted)
            {
                // The flush task can't fail today
                return Task.CompletedTask;
            }
            return FlushAsyncAwaited(awaitable, count, cancellationToken);
        }

        private async Task FlushAsyncAwaited(WritableBufferAwaitable awaitable, long count, CancellationToken cancellationToken)
        {
            // https://github.com/dotnet/corefxlab/issues/1334
            // Since the flush awaitable doesn't currently support multiple awaiters
            // we need to use a task to track the callbacks.
            // All awaiters get the same task
            lock (_flushLock)
            {
                if (_flushTcs == null || _flushTcs.Task.IsCompleted)
                {
                    _flushTcs = new TaskCompletionSource<object>();

                    awaitable.OnCompleted(_flushCompleted);
                }
            }

            _timeoutControl.StartTimingWrite(count);
            await _flushTcs.Task;
            _timeoutControl.StopTimingWrite();

            cancellationToken.ThrowIfCancellationRequested();
        }

        private void OnFlushCompleted()
        {
            _flushTcs.TrySetResult(null);
        }
    }
}
