// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2OutputProducer : IHttpOutputProducer
    {
        private readonly int _streamId;
        private readonly Http2FrameWriter _frameWriter;
        private readonly SafePipeWriterFlusher _flusher;

        // This should only be accessed via the FrameWriter. The connection-level output flow control is protected by the
        // FrameWriter's connection-level write lock.
        private readonly Http2StreamFlowControl _flowControl;

        private readonly object _dataWriterLock = new object();
        private readonly Pipe _dataPipe;
        private bool _completed;

        public Http2OutputProducer(
            int streamId,
            Http2FrameWriter frameWriter,
            Http2StreamFlowControl flowControl,
            ITimeoutControl timeoutControl,
            MemoryPool<byte> pool)
        {
            _streamId = streamId;
            _frameWriter = frameWriter;
            _flowControl = flowControl;
            _dataPipe = CreateDataPipe(pool);
            _flusher = new SafePipeWriterFlusher(_dataPipe.Writer, timeoutControl);
            _ = ProcessDataWrites();
        }

        public void Dispose()
        {
            lock (_dataWriterLock)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;

                // Complete with an exception to prevent an end of stream data frame from being sent
                // without an explicit call to WriteStreamSuffixAsync.
                _dataPipe.Writer.Complete(new ConnectionAbortedException());
            }
        }

        public void Abort(ConnectionAbortedException error)
        {
            // TODO: RST_STREAM?

            lock (_dataWriterLock)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                _dataPipe.Writer.Complete(error);
            }
        }
        public Task WriteAsync<T>(Func<PipeWriter, T, long> callback, T state)
        {
            throw new NotImplementedException();
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            lock (_dataWriterLock)
            {
                if (_completed)
                {
                    return Task.CompletedTask;
                }

                return _flusher.FlushAsync(0, this, cancellationToken);
            }
        }

        public Task Write100ContinueAsync()
        {
            lock (_dataWriterLock)
            {
                if (_completed)
                {
                    return Task.CompletedTask;
                }

                return _frameWriter.Write100ContinueAsync(_streamId);
            }
        }

        public void WriteResponseHeaders(int statusCode, string ReasonPhrase, HttpResponseHeaders responseHeaders)
        {
            lock (_dataWriterLock)
            {
                if (_completed)
                {
                    return;
                }

                // The HPACK header compressor is stateful, if we compress headers for an aborted stream we must send them.
                // Optimize for not compressing or sending them.
                _frameWriter.WriteResponseHeaders(_streamId, statusCode, responseHeaders);
            }
        }

        public Task WriteDataAsync(ReadOnlySpan<byte> data, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            lock (_dataWriterLock)
            {
                if (_completed)
                {
                    return Task.CompletedTask;
                }

                _dataPipe.Writer.Write(data);
                return _flusher.FlushAsync(data.Length, this, cancellationToken);
            }
        }

        public Task WriteStreamSuffixAsync()
        {
            lock (_dataWriterLock)
            {
                if (_completed)
                {
                    return Task.CompletedTask;
                }

                _completed = true;
                _dataPipe.Writer.Complete();
                return Task.CompletedTask;
            }
        }

        private async Task ProcessDataWrites()
        {
            var wroteData = false;

            try
            {
                ReadResult readResult;

                do
                {
                    readResult = await _dataPipe.Reader.ReadAsync();

                    await _frameWriter.WriteDataAsync(_streamId, _flowControl, readResult.Buffer, endStream: readResult.IsCompleted);
                    wroteData = true;

                    _dataPipe.Reader.AdvanceTo(readResult.Buffer.End);
                } while (!readResult.IsCompleted);
            }
            catch (ConnectionAbortedException)
            {
                // Writes should not throw for aborted connections.
            }
            catch (Exception ex)
            {
                Debug.Assert(false, ex.ToString());
            }

            _dataPipe.Reader.Complete();

            if (!wroteData)
            {
                // If no data was written, still make sure the headers are flushed by flushing the connection-level pipe.
                await _frameWriter.FlushAsync();
            }
        }

        private static Pipe CreateDataPipe(MemoryPool<byte> pool)
            => new Pipe(new PipeOptions
            (
                pool: pool,
                readerScheduler: PipeScheduler.Inline,
                writerScheduler: PipeScheduler.Inline,
                pauseWriterThreshold: 1,
                resumeWriterThreshold: 1,
                useSynchronizationContext: false,
                minimumSegmentSize: KestrelMemoryPool.MinimumSegmentSize
            ));
    }
}
