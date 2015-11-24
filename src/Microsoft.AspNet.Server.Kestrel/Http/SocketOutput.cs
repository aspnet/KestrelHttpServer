// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.AspNet.Server.Kestrel.Networking;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class SocketOutput : ISocketOutput
    {
        private const int _maxPendingWrites = 3;
        private const int _maxBytesPreCompleted = 65536;
        private const int _initialTaskQueues = 64;
        private const int _maxPooledWriteContexts = 32;

        private static WaitCallback _returnBlocks = (state) => ReturnBlocks((MemoryPoolBlock2)state);

        private readonly KestrelThread _thread;
        private readonly UvStreamHandle _socket;
        private readonly Connection _connection;
        private readonly long _connectionId;
        private readonly IKestrelTrace _log;

        // This locks all access to _tail, _isProducing and _returnFromOnProducingComplete.
        // _head does not require a lock, since it is only used in the ctor and uv thread.
        private readonly object _returnLock = new object();

        private MemoryPoolBlock2 _head;
        private MemoryPoolBlock2 _tail;

        private bool _isProducing;
        private MemoryPoolBlock2 _returnFromOnProducingComplete;

        // This locks access to to all of the below fields
        private readonly object _contextLock = new object();
        private bool _isDisposed = false;

        // The number of write operations that have been scheduled so far
        // but have not completed.
        private int _writesPending = 0;

        private int _numBytesPreCompleted = 0;
        private Exception _lastWriteError;
        private WriteContext _nextWriteContext;
        private readonly Queue<TaskCompletionSource<object>> _tasksPending;
        private readonly Queue<TaskCompletionSource<object>> _tasksCompleted;
        private readonly Queue<WriteContext> _writeContextPool;

        public SocketOutput(
            KestrelThread thread,
            UvStreamHandle socket,
            MemoryPool2 memory,
            Connection connection,
            long connectionId,
            IKestrelTrace log)
        {
            _thread = thread;
            _socket = socket;
            _connection = connection;
            _connectionId = connectionId;
            _log = log;
            _tasksPending = new Queue<TaskCompletionSource<object>>(_initialTaskQueues);
            _tasksCompleted = new Queue<TaskCompletionSource<object>>(_initialTaskQueues);
            _writeContextPool = new Queue<WriteContext>(_maxPooledWriteContexts);

            _head = memory.Lease();
            _tail = _head;
        }

        public Task WriteAsync(
            ArraySegment<byte> buffer,
            bool immediate = true,
            bool socketShutdownSend = false,
            bool socketDisconnect = false)
        {
            if (buffer.Count > 0)
            {
                var tail = ProducingStart();
                tail.CopyFrom(buffer);
                // We do our own accounting below
                ProducingComplete(tail, count: 0);
            }
            TaskCompletionSource<object> tcs = null;

            var scheduleWrite = false;

            lock (_contextLock)
            {
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
                    // immediate write, which is not eligable for instant completion above
                    tcs = new TaskCompletionSource<object>(buffer.Count);
                    _tasksPending.Enqueue(tcs);
                }

                if (_writesPending < _maxPendingWrites && immediate)
                {
                    scheduleWrite = true;
                    _writesPending++;
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
                        immediate: true,
                        socketShutdownSend: true,
                        socketDisconnect: false);
                    break;
                case ProduceEndType.SocketDisconnect:
                    WriteAsync(default(ArraySegment<byte>),
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
                Debug.Assert(!_isProducing);
                _isProducing = true;

                if (_tail == null)
                {
                    throw new IOException("The socket has been closed.");
                }

                return new MemoryPoolIterator2(_tail, _tail.End);
            }
        }

        public void ProducingComplete(MemoryPoolIterator2 end, int count)
        {
            var decreasePreCompleted = false;
            MemoryPoolBlock2 blockToReturn = null;

            lock (_returnLock)
            {
                Debug.Assert(_isProducing);
                _isProducing = false;

                if (_returnFromOnProducingComplete == null)
                {
                    _tail = end.Block;
                    _tail.End = end.Index;

                    if (count != 0)
                    {
                        decreasePreCompleted = true;
                    }
                }
                else
                {
                    blockToReturn = _returnFromOnProducingComplete;
                    _returnFromOnProducingComplete = null;
                }
            }

            if (decreasePreCompleted)
            {
                lock (_contextLock)
                {
                    _numBytesPreCompleted += count;
                }
            }


            if (blockToReturn != null)
            {
                ThreadPool.QueueUserWorkItem(_returnBlocks, blockToReturn);
            }
        }

        private static void ReturnBlocks(MemoryPoolBlock2 block)
        {
            while(block != null)
            {
                var returningBlock = block;
                block = returningBlock.Next;

                returningBlock.Pool?.Return(returningBlock);
            }
        }

        private void ScheduleWrite()
        {
            _thread.Post(_this => _this.WriteAllPending(), this);
        }

        // This is called on the libuv event loop
        private void WriteAllPending()
        {
            WriteContext writingContext;

            lock (_contextLock)
            {
                if (_nextWriteContext != null)
                {
                    writingContext = _nextWriteContext;
                    _nextWriteContext = null;
                }
                else
                {
                    _writesPending--;
                    return;
                }
            }

            try
            {
                writingContext.DoWriteIfNeeded();
            }
            catch
            {
                lock (_contextLock)
                {
                    // Lock instead of using Interlocked.Decrement so _writesSending
                    // doesn't change in the middle of executing other synchronized code.
                    _writesPending--;
                }

                throw;
            }
        }

        // This is called on the libuv event loop
        private void OnWriteCompleted(WriteContext writeContext)
        {
            var bytesWritten = writeContext.ByteCount;
            var status = writeContext.WriteStatus;
            var error = writeContext.WriteError;


            if (error != null)
            {
                _lastWriteError = new IOException(error.Message, error);

                // Abort the connection for any failed write.
                _connection.Abort();
            }

            bool scheduleWrite = false;

            lock (_contextLock)
            {
                PoolWriteContext(writeContext);
                if (_nextWriteContext != null)
                {
                    scheduleWrite = true;
                }
                else
                {
                    _writesPending--;
                }

                // _numBytesPreCompleted can temporarily go negative in the event there are
                // completed writes that we haven't triggered callbacks for yet.
                _numBytesPreCompleted -= bytesWritten;

                // bytesLeftToBuffer can be greater than _maxBytesPreCompleted
                // This allows large writes to complete once they've actually finished.
                var bytesLeftToBuffer = _maxBytesPreCompleted - _numBytesPreCompleted;
                while (_tasksPending.Count > 0 &&
                       (int)(_tasksPending.Peek().Task.AsyncState) <= bytesLeftToBuffer)
                {
                    var tcs = _tasksPending.Dequeue();
                    var bytesToWrite = (int)tcs.Task.AsyncState;

                    _numBytesPreCompleted += bytesToWrite;
                    bytesLeftToBuffer -= bytesToWrite;

                    _tasksCompleted.Enqueue(tcs);
                }
            }

            while (_tasksCompleted.Count > 0)
            {
                var tcs = _tasksCompleted.Dequeue();
                if (_lastWriteError == null)
                {
                    ThreadPool.QueueUserWorkItem(
                        (o) => ((TaskCompletionSource<object>)o).SetResult(null),
                        tcs);
                }
                else
                {
                    // error is closure captured 
                    ThreadPool.QueueUserWorkItem(
                        (o) => ((TaskCompletionSource<object>)o).SetException(_lastWriteError),
                        tcs);
                }
            }

            _log.ConnectionWriteCallback(_connectionId, status);

            if (scheduleWrite)
            {
                WriteAllPending();
            }

            _tasksCompleted.Clear();
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

                    returnBlock.Pool?.Return(returnBlock);
                }

                if (_isProducing)
                {
                    _returnFromOnProducingComplete = _tail;
                }
                else
                {
                    _tail.Pool?.Return(_tail);
                }

                _head = null;
                _tail = null;
            }
        }

        private void PoolWriteContext(WriteContext writeContext)
        {
            // called inside _contextLock
            if (!_isDisposed && _writeContextPool.Count < _maxPooledWriteContexts)
            {
                writeContext.Reset();
                _writeContextPool.Enqueue(writeContext);
            }
            else
            {
                writeContext.Dispose();
            }
        }

        public void Dispose()
        {
            lock (_contextLock)
            {
                _isDisposed = true;
                while (_writeContextPool.Count > 0)
                {
                    _writeContextPool.Dequeue().Dispose();
                }
            }
        }

        void ISocketOutput.Write(ArraySegment<byte> buffer, bool immediate)
        {
            var task = WriteAsync(buffer, immediate);

            if (task.Status == TaskStatus.RanToCompletion)
            {
                return;
            }
            else
            {
                task.GetAwaiter().GetResult();
            }
        }

        Task ISocketOutput.WriteAsync(ArraySegment<byte> buffer, bool immediate, CancellationToken cancellationToken)
        {
            return WriteAsync(buffer, immediate);
        }

        private class WriteContext : IDisposable
        {
            private static WaitCallback _returnWrittenBlocks = (state) => ReturnWrittenBlocks((MemoryPoolBlock2)state);

            private MemoryPoolIterator2 _lockedStart;
            private MemoryPoolIterator2 _lockedEnd;
            private int _bufferCount;
            public int ByteCount;

            public SocketOutput Self;

            public bool SocketShutdownSend;
            public bool SocketDisconnect;

            public int WriteStatus;
            public Exception WriteError;

            private UvWriteReq _writeReq;

            public int ShutdownSendStatus;

            public WriteContext(SocketOutput self)
            {
                Self = self;
                _writeReq = new UvWriteReq(Self._log);
                _writeReq.Init(Self._thread.Loop);
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

                _writeReq.Write(Self._socket, _lockedStart, _lockedEnd, _bufferCount, (_writeReq, status, error, state) =>
                {
                    var _this = (WriteContext)state;
                    _this.ScheduleReturnFullyWrittenBlocks();
                    _this.WriteStatus = status;
                    _this.WriteError = error;
                    _this.DoShutdownIfNeeded();
                }, this);

                Self._head = _lockedEnd.Block;
                if (Self._head != null)
                {
                    // Avoid shutdown race
                    Self._head.Start = _lockedEnd.Index;
                }
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
                if (SocketDisconnect == false)
                {
                    Complete();
                    return;
                }
                else if (Self._socket.IsClosed)
                {
                    Self.Dispose();
                    Complete();
                    return;
                }

                Self._socket.Dispose();
                Self.ReturnAllBlocks();
                Self.Dispose();
                Self._log.ConnectionStop(Self._connectionId);
                Complete();
            }

            public void Complete()
            {
                Self.OnWriteCompleted(this);
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
                    returnBlock.Pool?.Return(returnBlock);
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

                if (_lockedStart.Block == _lockedEnd.Block)
                {
                    ByteCount = _lockedEnd.Index - _lockedStart.Index;
                    _bufferCount = 1;
                    return;
                }

                ByteCount = _lockedStart.Block.Data.Offset + _lockedStart.Block.Data.Count - _lockedStart.Index;
                _bufferCount = 1;

                for (var block = _lockedStart.Block.Next; block != _lockedEnd.Block; block = block.Next)
                {
                    ByteCount += block.Data.Count;
                    _bufferCount++;
                }

                ByteCount += _lockedEnd.Index - _lockedEnd.Block.Data.Offset;
                _bufferCount++;
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

            public void Dispose()
            {
                _writeReq.Dispose();
            }
        }
    }
}
