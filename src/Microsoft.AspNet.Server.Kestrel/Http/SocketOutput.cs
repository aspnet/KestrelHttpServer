// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.AspNet.Server.Kestrel.Networking;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class SocketOutput : ISocketOutput
    {
        public const int MaxPooledWriteReqs = 1024;

        private const int _maxBytesPreCompleted = 65536;
        private const int _initialTaskQueues = 64;
        private const int _maxPooledWriteContexts = 32;

        private static readonly WaitCallback _returnBlocks = (state) => ReturnBlocks((MemoryPoolBlock2)state);
        private static readonly Action<object> _connectionCancellation = (state) => ((SocketOutput)state).CancellationTriggered();

        private readonly KestrelThread _thread;
        private readonly UvStreamHandle _socket;
        private readonly Connection _connection;
        private readonly long _connectionId;
        private readonly IKestrelTrace _log;
        private readonly IThreadPool _threadPool;

        // This locks all access to _tail and _lastStart.
        // _head does not require a lock, since it is only used in the ctor and uv thread.
        private readonly object _returnLock = new object();

        private MemoryPoolBlock2 _head;
        private MemoryPoolBlock2 _tail;

        private MemoryPoolIterator2 _lastStart;

        // This locks access to to all of the below fields
        private readonly object _contextLock = new object();

        // The number of write operations that have been scheduled so far
        // but have not completed.
        private bool _writePending = false;
        private int _numBytesPreCompleted = 0;
        private Exception _lastWriteError;
        private WriteContext _nextWriteContext;
        private readonly Queue<WaitingTask> _tasksPending;
        private readonly Queue<WriteContext> _writeContextPool;
        private readonly Queue<UvWriteReq> _writeReqPool;

        public SocketOutput(
            KestrelThread thread,
            UvStreamHandle socket,
            MemoryPool2 memory,
            Connection connection,
            long connectionId,
            IKestrelTrace log,
            IThreadPool threadPool,
            Queue<UvWriteReq> writeReqPool)
        {
            _thread = thread;
            _socket = socket;
            _connection = connection;
            _connectionId = connectionId;
            _log = log;
            _threadPool = threadPool;
            _tasksPending = new Queue<WaitingTask>(_initialTaskQueues);
            _writeContextPool = new Queue<WriteContext>(_maxPooledWriteContexts);
            _writeReqPool = writeReqPool;

            _head = memory.Lease();
            _tail = _head;
        }

        public Task WriteAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken,
            bool immediate = true,
            bool chunk = false,
            bool socketShutdownSend = false,
            bool socketDisconnect = false)
        {
            TaskCompletionSource<object> tcs = null;
            var scheduleWrite = false;

            lock (_contextLock)
            {
                if (buffer.Count > 0)
                {
                    var tail = ProducingStart();
                    if (tail.IsDefault)
                    {
                        if (cancellationToken.CanBeCanceled)
                        {
                            return TaskUtilities.GetCancelledTask(
                                cancellationToken.IsCancellationRequested ?
                                cancellationToken :
                                new CancellationToken(true));
                        }
                        else
                        {
                            return TaskUtilities.CompletedTask;
                        }
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

                if (!immediate)
                {
                    // immediate==false calls always return complete tasks, because there is guaranteed
                    // to be a subsequent immediate==true call which will go down one of the previous code-paths
                    _numBytesPreCompleted += buffer.Count;
                }
                else if (_lastWriteError == null &&
                        _tasksPending.Count == 0 &&
                        _numBytesPreCompleted + buffer.Count <= _maxBytesPreCompleted)
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
                            _connection.Abort();

                            return TaskUtilities.GetCancelledTask(cancellationToken);
                        }
                        else
                        {
                            // immediate write, which is not eligable for instant completion above
                            tcs = new TaskCompletionSource<object>();
                            _tasksPending.Enqueue(new WaitingTask()
                            {
                                CancellationRegistration = cancellationToken.Register(_connectionCancellation, this),
                                BytesToWrite = buffer.Count,
                                CompletionSource = tcs
                            });
                        }
                    }
                    else
                    {
                        tcs = new TaskCompletionSource<object>();
                        _tasksPending.Enqueue(new WaitingTask() {
                            BytesToWrite = buffer.Count,
                            CompletionSource = tcs
                        });
                    }
                }

                if (!_writePending && immediate)
                {
                    _writePending = true;
                    scheduleWrite = true;
                }
            }

            if (scheduleWrite)
            {
                ScheduleWrite();
            }

            // Return TaskCompletionSource's Task if set, otherwise completed Task 
            return tcs?.Task ?? TaskUtilities.CompletedTask;
        }

        public void End(ProduceEndType endType)
        {
            switch (endType)
            {
                case ProduceEndType.SocketShutdownSend:
                    WriteAsync(default(ArraySegment<byte>),
                        default(CancellationToken),
                        immediate: true,
                        socketShutdownSend: true,
                        socketDisconnect: false);
                    break;
                case ProduceEndType.SocketDisconnect:
                    WriteAsync(default(ArraySegment<byte>),
                        default(CancellationToken),
                        immediate: true,
                        socketShutdownSend: false,
                        socketDisconnect: true);
                    break;
            }
        }

        public MemoryPoolIterator2 ProducingStart()
        {
            lock (_returnLock)
            {
                Debug.Assert(_lastStart.IsDefault);

                if (_tail == null)
                {
                    return default(MemoryPoolIterator2);
                }

                _lastStart = new MemoryPoolIterator2(_tail, _tail.End);

                return _lastStart;
            }
        }

        public void ProducingComplete(MemoryPoolIterator2 end)
        {
            Debug.Assert(!_lastStart.IsDefault);

            int bytesProduced, buffersIncluded;
            BytesBetween(_lastStart, end, out bytesProduced, out buffersIncluded);

            lock (_contextLock)
            {
                _numBytesPreCompleted += bytesProduced;
            }

            ProducingCompleteNoPreComplete(end);
        }

        private void ProducingCompleteNoPreComplete(MemoryPoolIterator2 end)
        {
            MemoryPoolBlock2 blockToReturn = null;

            lock (_returnLock)
            {
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

                _lastStart = default(MemoryPoolIterator2);
            }

            if (blockToReturn != null)
            {
                ThreadPool.QueueUserWorkItem(_returnBlocks, blockToReturn);
            }
        }

        private void CancellationTriggered()
        {
            lock (_contextLock)
            {
                // Abort the connection for any failed write
                // Queued on threadpool so get it in as first op.
                _connection?.Abort();

                CancelOutstandingTasks(new TaskCanceledException("The request was aborted"));
            }
        }

        private static void ReturnBlocks(MemoryPoolBlock2 block)
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
            _thread.Post(_this => _this.WriteAllPending(), this);
        }

        // This is called on the libuv event loop
        private void WriteAllPending()
        {
            WriteContext writingContext = null;

            if (Monitor.TryEnter(_contextLock))
            {
                _writePending = false;

                if (_nextWriteContext != null)
                {
                    writingContext = _nextWriteContext;
                    _nextWriteContext = null;
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
        // This is always called with the _contextLock already acquired
        private void OnWriteCompleted(WriteContext writeContext)
        {
            var bytesWritten = writeContext.ByteCount;
            var status = writeContext.WriteStatus;
            var error = writeContext.WriteError;

            PoolWriteContext(writeContext);

            if (error == null)
            {
                // _numBytesPreCompleted can temporarily go negative in the event there are
                // completed writes that we haven't triggered callbacks for yet.
                _numBytesPreCompleted -= bytesWritten;

                CompleteFinishedWrites(status);
            }
            else if (error != null)
            {
                // Abort the connection for any failed write
                // Queued on threadpool so get it in as first op.
                _connection.Abort();

                CancelOutstandingTasks(error);
            }
        }

        private void CompleteFinishedWrites(int status)
        {
            // bytesLeftToBuffer can be greater than _maxBytesPreCompleted
            // This allows large writes to complete once they've actually finished.
            var bytesLeftToBuffer = _maxBytesPreCompleted - _numBytesPreCompleted;
            while (_tasksPending.Count > 0 &&
                   (_tasksPending.Peek().BytesToWrite) <= bytesLeftToBuffer)
            {
                var waitingTask = _tasksPending.Dequeue();
                var bytesToWrite = waitingTask.BytesToWrite;

                _numBytesPreCompleted += bytesToWrite;
                bytesLeftToBuffer -= bytesToWrite;

                // Dispose registration if there is one
                waitingTask.CancellationRegistration?.Dispose();

                _threadPool.Complete(waitingTask.CompletionSource);
            }

            _log.ConnectionWriteCallback(_connectionId, status);
        }

        private void CancelOutstandingTasks(Exception error)
        {
            _lastWriteError = error;

            while (_tasksPending.Count > 0)
            {
                var waitingTask = _tasksPending.Dequeue();

                if (waitingTask.CancellationRegistration != null)
                {
                    // Only Cancellation tokens which accept cancellation
                    // therefore have a registration should be cancelled
                    waitingTask.CancellationRegistration.Dispose();

                    _threadPool.Cancel(waitingTask.CompletionSource);
                }
                else
                {
                    _threadPool.Complete(waitingTask.CompletionSource);
                }
            }

            _log.ConnectionError(_connectionId, error);
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
                    _tail.Pool.Return(_tail);
                }

                _head = null;
                _tail = null;
            }
        }

        private void PoolWriteContext(WriteContext writeContext)
        {
            // called inside _contextLock
            if (_writeContextPool.Count < _maxPooledWriteContexts)
            {
                writeContext.Reset();
                _writeContextPool.Enqueue(writeContext);
            }
        }

        void ISocketOutput.Write(ArraySegment<byte> buffer, bool immediate, bool chunk)
        {
            var task = WriteAsync(buffer, CancellationToken.None, immediate, chunk);

            if (task.Status == TaskStatus.RanToCompletion)
            {
                return;
            }
            else
            {
                task.GetAwaiter().GetResult();
            }
        }

        Task ISocketOutput.WriteAsync(ArraySegment<byte> buffer, bool immediate, bool chunk, CancellationToken cancellationToken)
        {
            if (cancellationToken.CanBeCanceled)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _connection?.Abort();
                    return TaskUtilities.GetCancelledTask(cancellationToken);
                }
                else if (_lastWriteError != null || _socket.IsClosed)
                {
                    return TaskUtilities.GetCancelledTask(new CancellationToken(true));
                }
            }
            else if (_lastWriteError != null || _socket.IsClosed)
            {
                _log.ConnectionDisconnectedWrite(_connectionId, buffer.Count, _lastWriteError);

                return TaskUtilities.CompletedTask;
            }

            return WriteAsync(buffer, cancellationToken, immediate, chunk);
        }

        private static void BytesBetween(MemoryPoolIterator2 start, MemoryPoolIterator2 end, out int bytes, out int buffers)
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
            private static WaitCallback _returnWrittenBlocks = (state) => ReturnWrittenBlocks((MemoryPoolBlock2)state);
            private static WaitCallback _completeWrite = (state) => ((WriteContext)state).CompleteOnThreadPool();

            private SocketOutput Self;
            private UvWriteReq _writeReq;
            private MemoryPoolIterator2 _lockedStart;
            private MemoryPoolIterator2 _lockedEnd;
            private int _bufferCount;

            public int ByteCount;
            public bool SocketShutdownSend;
            public bool SocketDisconnect;

            public int WriteStatus;
            public Exception WriteError;
            public int ShutdownSendStatus;

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

                // Sample values locally in case write completes inline
                // to allow block to be Reset and still complete this function
                var lockedEndBlock = _lockedEnd.Block;
                var lockedEndIndex = _lockedEnd.Index;

                if (Self._writeReqPool.Count > 0)
                {
                    _writeReq = Self._writeReqPool.Dequeue();
                }
                else
                {
                    _writeReq = new UvWriteReq(Self._log);
                    _writeReq.Init(Self._thread.Loop);
                }

                _writeReq.Write(Self._socket, _lockedStart, _lockedEnd, _bufferCount, (_writeReq, status, error, state) =>
                {
                    var writeContext = (WriteContext)state;
                    writeContext.PoolWriteReq(writeContext._writeReq);
                    writeContext._writeReq = null;
                    writeContext.ScheduleReturnFullyWrittenBlocks();
                    writeContext.WriteStatus = status;
                    writeContext.WriteError = error;
                    writeContext.DoShutdownIfNeeded();
                }, this);

                Self._head = lockedEndBlock;
                Self._head.Start = lockedEndIndex;
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

                var shutdownReq = new UvShutdownReq(Self._log);
                shutdownReq.Init(Self._thread.Loop);
                shutdownReq.Shutdown(Self._socket, (_shutdownReq, status, state) =>
                {
                    _shutdownReq.Dispose();
                    var _this = (WriteContext)state;
                    _this.ShutdownSendStatus = status;

                    _this.Self._log.ConnectionWroteFin(_this.Self._connectionId, status);

                    _this.DoDisconnectIfNeeded();
                }, this);
            }

            /// <summary>
            /// Third step: disconnect socket if needed, otherwise this work item is complete
            /// </summary>
            public void DoDisconnectIfNeeded()
            {
                if (SocketDisconnect == false || Self._socket.IsClosed)
                {
                    CompleteOnUvThread();
                    return;
                }

                Self._socket.Dispose();
                Self.ReturnAllBlocks();
                Self._log.ConnectionStop(Self._connectionId);
                CompleteOnUvThread();
            }

            public void CompleteOnUvThread()
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
                    ThreadPool.QueueUserWorkItem(_completeWrite, this);
                }
            }

            public void CompleteOnThreadPool()
            {
                lock (Self._contextLock)
                {
                    try
                    {
                        Self.OnWriteCompleted(this);
                    }
                    catch (Exception ex)
                    {
                        Self._log.LogError("SocketOutput.OnWriteCompleted", ex);
                    }
                }
            }

            private void PoolWriteReq(UvWriteReq writeReq)
            {
                if (Self._writeReqPool.Count < MaxPooledWriteReqs)
                {
                    Self._writeReqPool.Enqueue(writeReq);
                }
                else
                {
                    writeReq.Dispose();
                }
            }

            private void ScheduleReturnFullyWrittenBlocks()
            {
                var block = _lockedStart.Block;
                var end = _lockedEnd.Block;
                if (block == end)
                {
                    end.Unpin();
                    return;
                }

                while (block.Next != end)
                {
                    block = block.Next;
                    block.Unpin();
                }
                block.Next = null;

                ThreadPool.QueueUserWorkItem(_returnWrittenBlocks, _lockedStart.Block);
            }

            private static void ReturnWrittenBlocks(MemoryPoolBlock2 block)
            {
                while (block != null)
                {
                    var returnBlock = block;
                    block = block.Next;

                    returnBlock.Unpin();
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

                _lockedStart = new MemoryPoolIterator2(head, head.Start);
                _lockedEnd = new MemoryPoolIterator2(tail, tail.End);

                BytesBetween(_lockedStart, _lockedEnd, out ByteCount, out _bufferCount);
            }

            public void Reset()
            {
                _lockedStart = default(MemoryPoolIterator2);
                _lockedEnd = default(MemoryPoolIterator2);
                _bufferCount = 0;
                ByteCount = 0;

                SocketShutdownSend = false;
                SocketDisconnect = false;

                WriteStatus = 0;
                WriteError = null;

                ShutdownSendStatus = 0;
            }
        }

        private struct WaitingTask
        {
            public IDisposable CancellationRegistration;
            public int BytesToWrite;
            public TaskCompletionSource<object> CompletionSource;
        }
    }
}
