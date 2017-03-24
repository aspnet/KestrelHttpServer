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

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class SocketOutput : ISocketOutput
    {
        private const int _maxPendingWrites = 3;
        // There should be never be more WriteContexts than the max ongoing writes +  1 for the next write to be scheduled.
        private const int _maxPooledWriteContexts = _maxPendingWrites + 1;
        // Well behaved WriteAsync users should await returned task, so there is no need to allocate more per connection by default
        private const int _initialTaskQueues = 1;

        private static readonly ArraySegment<byte> _emptyData = new ArraySegment<byte>(new byte[0]);
        private static readonly WaitCallback _returnBlocks = (state) => ReturnBlocks((MemoryPoolBlock)state);
        private static readonly Action<object> _connectionCancellation = (state) => ((SocketOutput)state).CancellationTriggered();

        private readonly KestrelThread _thread;
        private readonly UvStreamHandle _socket;
        private readonly Connection _connection;
        private readonly long? _maxBytesPreCompleted;
        private readonly string _connectionId;
        private readonly IKestrelTrace _log;
        private readonly IThreadPool _threadPool;

        // This locks all access to _tail, _head, _lastStart and _closed.
        private readonly object _returnLock = new object();

        private bool _closed;
        private MemoryPoolBlock _head;
        private MemoryPoolBlock _tail;
        private MemoryPoolIterator _lastStart;

        // This locks access to to all of the below fields
        private readonly object _contextLock = new object();

        // The number of write operations that have been scheduled so far
        // but have not completed.
        private int _ongoingWrites = 0;
        // Whether or not a write operation is pending to start on the uv thread.
        // If this is true, there is no reason to schedule another write even if
        // there aren't yet three ongoing write operations.
        private bool _postingWrite = false;

        private bool _cancelled = false;
        private long _numBytesPreCompleted = 0;
        private Exception _lastWriteError;
        private WriteContext _nextWriteContext;
        private readonly Queue<WaitingTask> _tasksPending;
        private readonly Queue<WriteContext> _writeContextPool;
        private readonly WriteReqPool _writeReqPool;

        public SocketOutput(
            KestrelThread thread,
            UvStreamHandle socket,
            Connection connection,
            string connectionId,
            IKestrelTrace log,
            IThreadPool threadPool)
        {
            _thread = thread;
            _socket = socket;
            _connection = connection;
            _connectionId = connectionId;
            _log = log;
            _threadPool = threadPool;
            _tasksPending = new Queue<WaitingTask>(_initialTaskQueues);
            _writeContextPool = new Queue<WriteContext>(_maxPooledWriteContexts);
            _writeReqPool = thread.WriteReqPool;
            _maxBytesPreCompleted = connection.ServerOptions.Limits.MaxResponseBufferSize;
        }

        public Task WriteAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken,
            bool chunk = false,
            bool socketShutdownSend = false,
            bool socketDisconnect = false,
            bool isSync = false)
        {
            TaskCompletionSource<object> tcs = null;
            var scheduleWrite = false;

            lock (_contextLock)
            {
                if (_socket.IsClosed)
                {
                    _log.ConnectionDisconnectedWrite(_connectionId, buffer.Count, _lastWriteError);

                    return TaskCache.CompletedTask;
                }

                if (buffer.Count > 0)
                {
                    var tail = ProducingStart();
                    if (tail.IsDefault)
                    {
                        return TaskCache.CompletedTask;
                    }

                    if (chunk)
                    {
                        _numBytesPreCompleted += ChunkWriter.WriteBeginChunkBytes(ref tail, buffer.Count);
                    }

                    tail.CopyFrom(buffer);

                    if (chunk)
                    {
                        ChunkWriter.WriteEndChunkBytes(ref tail);
                        _numBytesPreCompleted += 2;
                    }

                    // We do our own accounting below
                    ProducingCompleteNoPreComplete(tail);
                }

                if (_nextWriteContext == null)
                {
                    if (_writeContextPool.Count > 0)
                    {
                        _nextWriteContext = _writeContextPool.Dequeue();
                    }
                    else
                    {
                        _nextWriteContext = new WriteContext(this);
                    }
                }

                if (socketShutdownSend)
                {
                    _nextWriteContext.SocketShutdownSend = true;
                }
                if (socketDisconnect)
                {
                    _nextWriteContext.SocketDisconnect = true;
                }

                if (!_maxBytesPreCompleted.HasValue || _numBytesPreCompleted + buffer.Count <= _maxBytesPreCompleted.Value)
                {
                    // Complete the write task immediately if all previous write tasks have been completed,
                    // the buffers haven't grown too large, and the last write to the socket succeeded.
                    _numBytesPreCompleted += buffer.Count;
                }
                else
                {
                    if (cancellationToken.CanBeCanceled)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _connection.AbortAsync();
                            _cancelled = true;
                            return Task.FromCanceled(cancellationToken);
                        }
                        else
                        {
                            // immediate write, which is not eligable for instant completion above
                            tcs = new TaskCompletionSource<object>();
                            _tasksPending.Enqueue(new WaitingTask()
                            {
                                CancellationToken = cancellationToken,
                                CancellationRegistration = cancellationToken.SafeRegister(_connectionCancellation, this),
                                BytesToWrite = buffer.Count,
                                CompletionSource = tcs
                            });
                        }
                    }
                    else
                    {
                        tcs = new TaskCompletionSource<object>();
                        _tasksPending.Enqueue(new WaitingTask() {
                            IsSync = isSync,
                            BytesToWrite = buffer.Count,
                            CompletionSource = tcs
                        });
                    }
                }

                if (!_postingWrite && _ongoingWrites < _maxPendingWrites)
                {
                    _postingWrite = true;
                    _ongoingWrites++;
                    scheduleWrite = true;
                }
            }

            if (scheduleWrite)
            {
                ScheduleWrite();
            }

            // Return TaskCompletionSource's Task if set, otherwise completed Task
            return tcs?.Task ?? TaskCache.CompletedTask;
        }

        public void End(ProduceEndType endType)
        {
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
        }

        public MemoryPoolIterator ProducingStart()
        {
            lock (_returnLock)
            {
                Debug.Assert(_lastStart.IsDefault);

                if (_closed)
                {
                    return default(MemoryPoolIterator);
                }

                if (_tail == null)
                {
                    _head = _thread.Memory.Lease();
                    _tail = _head;
                }

                _lastStart = new MemoryPoolIterator(_tail, _tail.End);

                return _lastStart;
            }
        }

        public void ProducingComplete(MemoryPoolIterator end)
        {
            if (_lastStart.IsDefault)
            {
                return;
            }

            int bytesProduced, buffersIncluded;
            BytesBetween(_lastStart, end, out bytesProduced, out buffersIncluded);

            lock (_contextLock)
            {
                _numBytesPreCompleted += bytesProduced;
            }

            ProducingCompleteNoPreComplete(end);
        }

        private void ProducingCompleteNoPreComplete(MemoryPoolIterator end)
        {
            MemoryPoolBlock blockToReturn = null;

            lock (_returnLock)
            {
                // Both ProducingComplete and WriteAsync should not call this method
                // if _lastStart was not set.
                Debug.Assert(!_lastStart.IsDefault);

                // If the socket has been closed, return the produced blocks
                // instead of advancing the now non-existent tail.
                if (_tail != null)
                {
                    _tail = end.Block;
                    _tail.End = end.Index;
                }
                else
                {
                    blockToReturn = _lastStart.Block;
                }

                _lastStart = default(MemoryPoolIterator);
            }

            if (blockToReturn != null)
            {
                _threadPool.UnsafeRun(_returnBlocks, blockToReturn);
            }
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

                    CompleteAllWrites();

                    _log.ConnectionError(_connectionId, new TaskCanceledException("Write operation canceled. Aborting connection."));
                }
            }
        }

        private static void ReturnBlocks(MemoryPoolBlock block)
        {
            while (block != null)
            {
                var returningBlock = block;
                block = returningBlock.Next;

                returningBlock.Pool.Return(returningBlock);
            }
        }

        private void ScheduleWrite()
        {
            _thread.Post(state => ((SocketOutput)state).WriteAllPending(), this);
        }

        // This is called on the libuv event loop
        private void WriteAllPending()
        {
            WriteContext writingContext = null;

            if (Monitor.TryEnter(_contextLock))
            {
                _postingWrite = false;

                if (_nextWriteContext != null)
                {
                    writingContext = _nextWriteContext;
                    _nextWriteContext = null;
                }
                else
                {
                    _ongoingWrites--;
                }

                Monitor.Exit(_contextLock);
            }
            else
            {
                ScheduleWrite();
            }

            if (writingContext != null)
            {
                writingContext.DoWriteIfNeeded();
            }
        }

        // This may called on the libuv event loop
        private void OnWriteCompleted(WriteContext writeContext)
        {
            // Called inside _contextLock
            var bytesWritten = writeContext.ByteCount;
            var status = writeContext.WriteStatus;
            var error = writeContext.WriteError;

            if (error != null)
            {
                // Abort the connection for any failed write
                // Queued on threadpool so get it in as first op.
                _connection.AbortAsync();
                _cancelled = true;
                _lastWriteError = error;
            }

            PoolWriteContext(writeContext);

            // _numBytesPreCompleted can temporarily go negative in the event there are
            // completed writes that we haven't triggered callbacks for yet.
            _numBytesPreCompleted -= bytesWritten;

            if (error == null)
            {
                CompleteFinishedWrites(status);
                _log.ConnectionWriteCallback(_connectionId, status);
            }
            else
            {
                CompleteAllWrites();

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

            if (!_postingWrite && _nextWriteContext != null)
            {
                _postingWrite = true;
                ScheduleWrite();
            }
            else
            {
                _ongoingWrites--;
            }
        }

        private void CompleteNextWrite(ref long bytesLeftToBuffer)
        {
            // Called inside _contextLock
            var waitingTask = _tasksPending.Dequeue();
            var bytesToWrite = waitingTask.BytesToWrite;

            _numBytesPreCompleted += bytesToWrite;
            bytesLeftToBuffer -= bytesToWrite;

            // Dispose registration if there is one
            waitingTask.CancellationRegistration?.Dispose();

            if (waitingTask.CancellationToken.IsCancellationRequested)
            {
                if (waitingTask.IsSync)
                {
                    waitingTask.CompletionSource.TrySetCanceled();
                }
                else
                {
                    _threadPool.Cancel(waitingTask.CompletionSource);
                }
            }
            else
            {
                if (waitingTask.IsSync)
                {
                    waitingTask.CompletionSource.TrySetResult(null);
                }
                else
                {
                    _threadPool.Complete(waitingTask.CompletionSource);
                }
            }
        }

        private void CompleteFinishedWrites(int status)
        {
            if (!_maxBytesPreCompleted.HasValue)
            {
                Debug.Assert(_tasksPending.Count == 0);
                return;
            }

            // Called inside _contextLock
            // bytesLeftToBuffer can be greater than _maxBytesPreCompleted
            // This allows large writes to complete once they've actually finished.
            var bytesLeftToBuffer = _maxBytesPreCompleted.Value - _numBytesPreCompleted;
            while (_tasksPending.Count > 0 &&
                   (_tasksPending.Peek().BytesToWrite) <= bytesLeftToBuffer)
            {
                CompleteNextWrite(ref bytesLeftToBuffer);
            }
        }

        private void CompleteAllWrites()
        {
            if (!_maxBytesPreCompleted.HasValue)
            {
                Debug.Assert(_tasksPending.Count == 0);
                return;
            }

            // Called inside _contextLock
            var bytesLeftToBuffer = _maxBytesPreCompleted.Value - _numBytesPreCompleted;
            while (_tasksPending.Count > 0)
            {
                CompleteNextWrite(ref bytesLeftToBuffer);
            }
        }

        // This is called on the libuv event loop
        private void ReturnAllBlocks()
        {
            lock (_returnLock)
            {
                var block = _head;
                while (block != _tail)
                {
                    var returnBlock = block;
                    block = block.Next;

                    returnBlock.Pool.Return(returnBlock);
                }

                // Only return the _tail if we aren't between ProducingStart/Complete calls
                if (_lastStart.IsDefault)
                {
                    _tail?.Pool.Return(_tail);
                }

                _head = null;
                _tail = null;
                _closed = true;
            }
        }

        private void PoolWriteContext(WriteContext writeContext)
        {
            // Called inside _contextLock
            if (_writeContextPool.Count < _maxPooledWriteContexts)
            {
                writeContext.Reset();
                _writeContextPool.Enqueue(writeContext);
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
                return Task.FromCanceled(cancellationToken);
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

        private static void BytesBetween(MemoryPoolIterator start, MemoryPoolIterator end, out int bytes, out int buffers)
        {
            if (start.Block == end.Block)
            {
                bytes = end.Index - start.Index;
                buffers = 1;
                return;
            }

            bytes = start.Block.Data.Offset + start.Block.Data.Count - start.Index;
            buffers = 1;

            for (var block = start.Block.Next; block != end.Block; block = block.Next)
            {
                bytes += block.Data.Count;
                buffers++;
            }

            bytes += end.Index - end.Block.Data.Offset;
            buffers++;
        }

        private class WriteContext
        {
            private static readonly WaitCallback _returnWrittenBlocks = (state) => ReturnWrittenBlocks((MemoryPoolBlock)state);
            private static readonly WaitCallback _completeWrite = (state) => ((WriteContext)state).CompleteOnThreadPool();

            private SocketOutput Self;
            private UvWriteReq _writeReq;
            private MemoryPoolIterator _lockedStart;
            private MemoryPoolIterator _lockedEnd;
            private int _bufferCount;

            public int ByteCount;
            public bool SocketShutdownSend;
            public bool SocketDisconnect;

            public int WriteStatus;
            public Exception WriteError;

            public WriteContext(SocketOutput self)
            {
                Self = self;
            }

            /// <summary>
            /// First step: initiate async write if needed, otherwise go to next step
            /// </summary>
            public void DoWriteIfNeeded()
            {
                LockWrite();

                if (ByteCount == 0 || Self._socket.IsClosed)
                {
                    DoShutdownIfNeeded();
                    return;
                }

                // Update _head immediate after write is "locked", so the block returning logic
                // works correctly when run inline in the write callback.
                Self._head = _lockedEnd.Block;
                Self._head.Start = _lockedEnd.Index;

                _writeReq = Self._writeReqPool.Allocate();

                _writeReq.Write(Self._socket, _lockedStart, _lockedEnd, _bufferCount, (req, status, error, state) =>
                {
                    var writeContext = (WriteContext)state;
                    writeContext.PoolWriteReq(writeContext._writeReq);
                    writeContext._writeReq = null;
                    writeContext.ScheduleReturnWrittenBlocks();
                    writeContext.WriteStatus = status;
                    writeContext.WriteError = error;
                    writeContext.DoShutdownIfNeeded();
                }, this);
            }

            /// <summary>
            /// Second step: initiate async shutdown if needed, otherwise go to next step
            /// </summary>
            public void DoShutdownIfNeeded()
            {
                if (SocketShutdownSend == false || Self._socket.IsClosed)
                {
                    DoDisconnectIfNeeded();
                    return;
                }

                Self._log.ConnectionWriteFin(Self._connectionId);

                var shutdownReq = new UvShutdownReq(Self._log);
                shutdownReq.Init(Self._thread.Loop);
                shutdownReq.Shutdown(Self._socket, (req, status, state) =>
                {
                    req.Dispose();

                    var writeContext = (WriteContext)state;
                    writeContext.Self._log.ConnectionWroteFin(writeContext.Self._connectionId, status);
                    writeContext.DoDisconnectIfNeeded();
                }, this);
            }

            /// <summary>
            /// Third step: disconnect socket if needed, otherwise this work item is complete
            /// </summary>
            private void DoDisconnectIfNeeded()
            {
                if (SocketDisconnect == false || Self._socket.IsClosed)
                {
                    CompleteWithContextLock();
                    return;
                }

                // Ensure all blocks are returned before calling OnSocketClosed
                // to ensure the MemoryPool doesn't get disposed too soon.
                Self.ReturnAllBlocks();
                Self._socket.Dispose();
                Self._connection.OnSocketClosed();
                Self._log.ConnectionStop(Self._connectionId);
                CompleteWithContextLock();
            }

            private void CompleteWithContextLock()
            {
                if (Monitor.TryEnter(Self._contextLock))
                {
                    try
                    {
                        Self.OnWriteCompleted(this);
                    }
                    finally
                    {
                        Monitor.Exit(Self._contextLock);
                    }
                }
                else
                {
                    Self._threadPool.UnsafeRun(_completeWrite, this);
                }
            }

            private void CompleteOnThreadPool()
            {
                lock (Self._contextLock)
                {
                    try
                    {
                        Self.OnWriteCompleted(this);
                    }
                    catch (Exception ex)
                    {
                        Self._log.LogError(0, ex, "SocketOutput.OnWriteCompleted");
                    }
                }
            }

            private void PoolWriteReq(UvWriteReq writeReq)
            {
                Self._writeReqPool.Return(writeReq);
            }

            private void ScheduleReturnWrittenBlocks()
            {
                var returnAll = false;

                lock (Self._returnLock)
                {
                    // If everything has been fully written, return _tail/_lockedEnd.
                    if (_lockedEnd.Block == Self._tail &&
                        _lockedEnd.Index == Self._tail.End &&
                        Self._lastStart.IsDefault)
                    {
                        Debug.Assert(Self._head == Self._tail);
                        Debug.Assert(Self._tail.Start == Self._tail.End);

                        Self._head = null;
                        Self._tail = null;
                        returnAll = true;
                    }
                }

                if (!returnAll)
                {
                    var block = _lockedStart.Block;
                    var end = _lockedEnd.Block;
                    if (block == end)
                    {
                        return;
                    }

                    while (block.Next != end)
                    {
                        block = block.Next;
                    }

                    // Set the Next pointer in the block before _lockedEnd.Block to null.
                    // This prevents _lockedEnd.Block from being returned if it isn't fully
                    // written, or it's still being written to.
                    block.Next = null;
                }

                Self._threadPool.UnsafeRun(_returnWrittenBlocks, _lockedStart.Block);
            }

            private static void ReturnWrittenBlocks(MemoryPoolBlock block)
            {
                while (block != null)
                {
                    var returnBlock = block;
                    block = block.Next;

                    returnBlock.Pool.Return(returnBlock);
                }
            }

            private void LockWrite()
            {
                var head = Self._head;
                var tail = Self._tail;

                if (head == null || tail == null)
                {
                    // ReturnAllBlocks has already bee called. Nothing to do here.
                    // Write will no-op since _byteCount will remain 0.
                    return;
                }

                _lockedStart = new MemoryPoolIterator(head, head.Start);
                _lockedEnd = new MemoryPoolIterator(tail, tail.End);

                BytesBetween(_lockedStart, _lockedEnd, out ByteCount, out _bufferCount);
            }

            public void Reset()
            {
                _lockedStart = default(MemoryPoolIterator);
                _lockedEnd = default(MemoryPoolIterator);
                _bufferCount = 0;
                ByteCount = 0;

                SocketShutdownSend = false;
                SocketDisconnect = false;

                WriteStatus = 0;
                WriteError = null;
            }
        }

        private struct WaitingTask
        {
            public bool IsSync;
            public int BytesToWrite;
            public CancellationToken CancellationToken;
            public IDisposable CancellationRegistration;
            public TaskCompletionSource<object> CompletionSource;
        }
    }
}
