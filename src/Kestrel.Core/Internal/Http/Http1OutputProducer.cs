// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public class Http1OutputProducer : IHttpOutputProducer
    {
        private static readonly ArraySegment<byte> _continueBytes = new ArraySegment<byte>(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"));
        private static readonly byte[] _bytesHttpVersion11 = Encoding.ASCII.GetBytes("HTTP/1.1 ");
        private static readonly byte[] _bytesEndHeaders = Encoding.ASCII.GetBytes("\r\n\r\n");
        private static readonly ArraySegment<byte> _endChunkedResponseBytes = new ArraySegment<byte>(Encoding.ASCII.GetBytes("0\r\n\r\n"));

        private readonly string _connectionId;
        private readonly ITimeoutControl _timeoutControl;
        private readonly IKestrelTrace _log;

        // This locks access to the output writing fields below
        protected readonly SemaphoreSlim _contextSemaphore = new SemaphoreSlim(1);

        private bool _completed = false;

        private readonly PipeWriter _pipeWriter;
        private readonly PipeReader _outputPipeReader;

        // https://github.com/dotnet/corefxlab/issues/1334
        // Pipelines don't support multiple awaiters on flush
        // this is temporary until it does
        private TaskCompletionSource<object> _flushTcs;
        private readonly object _flushLock = new object();
        private Action _flushCompleted;

        public Http1OutputProducer(
            PipeReader outputPipeReader,
            PipeWriter pipeWriter,
            string connectionId,
            IKestrelTrace log,
            ITimeoutControl timeoutControl)
        {
            _outputPipeReader = outputPipeReader;
            _pipeWriter = pipeWriter;
            _connectionId = connectionId;
            _timeoutControl = timeoutControl;
            _log = log;
            _flushCompleted = OnFlushCompleted;
        }

        public Task WriteDataAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            return WriteAsync(buffer, cancellationToken);
        }

        public Task WriteStreamSuffixAsync(CancellationToken cancellationToken)
        {
            return WriteAsync(_endChunkedResponseBytes, cancellationToken);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return WriteAsync(Constants.EmptyData, cancellationToken);
        }

        public void Write<T>(Action<PipeWriter, T> callback, T state)
        {
            _contextSemaphore.Wait();
            try
            {
                if (_completed)
                {
                    return;
                }

                var buffer = _pipeWriter;
                callback(buffer, state);
                buffer.Commit();
            }
            finally
            {
                _contextSemaphore.Release();
            }
        }

        public Task WriteAsync<T>(Action<PipeWriter, T> callback, T state)
        {
            var task = _contextSemaphore.WaitAsync();
#if NETCOREAPP2_1
            if (!task.IsCompletedSuccessfully)
            {
                return WriteAsync(task, callback, state);
            }
#else
            if (!task.IsCompleted || task.IsFaulted || task.IsCanceled)
            {
                return WriteAsync(task, callback, state);
            }
#endif
            return WriteAsyncWithLock(callback, state);
        }

        private async Task WriteAsync<T>(Task task, Action<PipeWriter, T> callback, T state)
        {
            await task;
            await WriteAsyncWithLock(callback, state);
        }

        private Task WriteAsyncWithLock<T>(Action<PipeWriter, T> callback, T state)
        {
            try
            {
                if (_completed)
                {
                    return Task.CompletedTask;
                }

                var buffer = _pipeWriter;
                callback(buffer, state);
                buffer.Commit();
            }
            finally
            {
                _contextSemaphore.Release();
            }

            return FlushAsync();
        }

#if NETCOREAPP2_1
        public async Task WriteAsync(Stream stream, long? count, CancellationToken cancellationToken)
        {
            const int maxBlockSize = 4032; // Should be: Environment.SystemPageSize https://github.com/dotnet/corefxlab/pull/2099

            var buffer = _pipeWriter;
            var remaining = count ?? long.MaxValue;
            int outputBytes;
            do
            {
                await _contextSemaphore.WaitAsync();
                try
                {
                    if (_completed)
                    {
                        return;
                    }

                    var memory = buffer.GetMemory(remaining > maxBlockSize ? maxBlockSize : (int)remaining);
                    // Read the stream directly into the output buffer
                    if (memory.Length > remaining)
                    {
                        memory = memory.Slice((int)remaining);
                    }
                    outputBytes = await stream.ReadAsync(memory, cancellationToken);
                    remaining -= outputBytes;
                    if (outputBytes > 0)
                    {
                        buffer.Advance(outputBytes);
                        buffer.Commit();
                    }
                }
                finally
                {
                    _contextSemaphore.Release();
                }

                // WriteAsync() - not forced flush here
                await buffer.FlushAsync(cancellationToken);
            } while (outputBytes > 0 && !cancellationToken.IsCancellationRequested);
            // FlushAsync() - forced flush; or better move it to outer loop

            if (count.HasValue && remaining > 0)
            {
                // throw
            }
        }
#else
        public Task WriteAsync(Stream stream, long? count, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
#endif

        public void WriteResponseHeaders(int statusCode, string reasonPhrase, HttpResponseHeaders responseHeaders)
        {
            _contextSemaphore.Wait();
            try
            {
                if (_completed)
                {
                    return;
                }

                var buffer = _pipeWriter;
                var writer = OutputWriter.Create(buffer);

                writer.Write(_bytesHttpVersion11);
                var statusBytes = ReasonPhrases.ToStatusBytes(statusCode, reasonPhrase);
                writer.Write(statusBytes);
                responseHeaders.CopyTo(ref writer);
                writer.Write(_bytesEndHeaders);
                buffer.Commit();
            }
            finally
            {
                _contextSemaphore.Release();
            }
        }

        public void Dispose()
        {
            _contextSemaphore.Wait();
            try
            {
                if (_completed)
                {
                    return;
                }

                _log.ConnectionDisconnect(_connectionId);
                _completed = true;
                _pipeWriter.Complete();
            }
            finally
            {
                _contextSemaphore.Release();
            }
        }

        public void Abort(Exception error)
        {
            _contextSemaphore.Wait();
            try
            {
                if (_completed)
                {
                    return;
                }

                _log.ConnectionDisconnect(_connectionId);
                _completed = true;

                _outputPipeReader.CancelPendingRead();
                _pipeWriter.Complete(error);
            }
            finally
            {
                _contextSemaphore.Release();
            }
        }

        public Task Write100ContinueAsync(CancellationToken cancellationToken)
        {
            return WriteAsync(_continueBytes, default(CancellationToken));
        }

        private Task WriteAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            var writableBuffer = default(PipeWriter);
            long bytesWritten = 0;

            _contextSemaphore.Wait();
            try
            {
                if (_completed)
                {
                    return Task.CompletedTask;
                }

                writableBuffer = _pipeWriter;
                var writer = OutputWriter.Create(writableBuffer);
                if (buffer.Count > 0)
                {
                    writer.Write(new ReadOnlySpan<byte>(buffer.Array, buffer.Offset, buffer.Count));
                    bytesWritten += buffer.Count;
                }

                writableBuffer.Commit();
            }
            finally
            {
                _contextSemaphore.Release();
            }

            return FlushAsync(writableBuffer, bytesWritten, cancellationToken);
        }

        // Single caller, at end of method - so inline
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Task FlushAsync(PipeWriter writableBuffer, long bytesWritten, CancellationToken cancellationToken)
        {
            var awaitable = writableBuffer.FlushAsync(cancellationToken);
            if (awaitable.IsCompleted)
            {
                // The flush task can't fail today
                return Task.CompletedTask;
            }
            return FlushAsyncAwaited(awaitable, bytesWritten, cancellationToken);
        }

        private async Task FlushAsyncAwaited(ValueAwaiter<FlushResult> awaitable, long count, CancellationToken cancellationToken)
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
