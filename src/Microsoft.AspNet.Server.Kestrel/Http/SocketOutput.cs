// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private const int _maxPooledWriteContexts = 16;
        private const int _maxPooledBufferQueues = 16;

        private readonly KestrelThread _thread;
        private readonly UvStreamHandle _socket;
        private readonly long _connectionId;
        private readonly IKestrelTrace _log;

        // This locks access to to all of the below fields
        private readonly object _lockObj = new object();
        private bool _isDisposed; 

        // The number of write operations that have been scheduled so far
        // but have not completed.
        private int _writesPending = 0;

        private int _numBytesPreCompleted = 0;
        private Exception _lastWriteError;
        private WriteContext _nextWriteContext;
        private readonly Queue<TaskCompletionSource<object>> _tasksPending;
        private readonly Queue<WriteContext> _writeContexts;

        public SocketOutput(KestrelThread thread, UvStreamHandle socket, long connectionId, IKestrelTrace log)
        {
            _thread = thread;
            _socket = socket;
            _connectionId = connectionId;
            _log = log;
            _tasksPending = new Queue<TaskCompletionSource<object>>(16);
            _writeContexts = new Queue<WriteContext>(_maxPooledWriteContexts);
        }

        public Task WriteAsync(
            ArraySegment<byte> buffer,
            bool immediate = true,
            bool socketShutdownSend = false,
            bool socketDisconnect = false)
        {
            //TODO: need buffering that works
            if (buffer.Array != null)
            {
                var copy = new byte[buffer.Count];
                Buffer.BlockCopy(buffer.Array, buffer.Offset, copy, 0, buffer.Count);
                buffer = new ArraySegment<byte>(copy);
                _log.ConnectionWrite(_connectionId, buffer.Count);
            }

            TaskCompletionSource<object> tcs = null;

            lock (_lockObj)
            {
                if (_nextWriteContext == null)
                {
                    if (_writeContexts.Count > 0)
                    {
                        _nextWriteContext = _writeContexts.Dequeue();
                    }
                    else
                    {
                        _nextWriteContext = new WriteContext(this);
                    }
                }

                if (buffer.Array != null)
                {
                    _nextWriteContext.Buffers.Enqueue(buffer);
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
                    ScheduleWrite();
                    _writesPending++;
                }
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

        private void ScheduleWrite()
        {
            _thread.Post(_this => _this.WriteAllPending(), this);
        }

        // This is called on the libuv event loop
        private void WriteAllPending()
        {
            WriteContext writingContext;

            lock (_lockObj)
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
                lock (_lockObj)
                {
                    // Lock instead of using Interlocked.Decrement so _writesSending
                    // doesn't change in the middle of executing other synchronized code.
                    _writesPending--;
                }

                throw;
            }
        }

        // This is called on the libuv event loop
        private void OnWriteCompleted(WriteContext write)
        {
            var status = write.WriteStatus;

            lock (_lockObj)
            {
                _lastWriteError = write.WriteError;

                if (_nextWriteContext != null)
                {
                    ScheduleWrite();
                }
                else
                {
                    _writesPending--;
                }

                foreach (var writeBuffer in write.Buffers)
                {
                    // _numBytesPreCompleted can temporarily go negative in the event there are
                    // completed writes that we haven't triggered callbacks for yet.
                    _numBytesPreCompleted -= writeBuffer.Count;
                }
                
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

                    if (write.WriteError == null)
                    {
                        ThreadPool.QueueUserWorkItem(
                            (o) => ((TaskCompletionSource<object>)o).SetResult(null), 
                            tcs);
                    }
                    else
                    {
                        var error = write.WriteError;
                        // error is closure captured 
                        ThreadPool.QueueUserWorkItem(
                            (o) => ((TaskCompletionSource<object>)o).SetException(error), 
                            tcs);
                    }
                }

                if (_writeContexts.Count < _maxPooledWriteContexts 
                    && write.Buffers.Count <= _maxPooledBufferQueues
                    && !_isDisposed)
                {
                    write.Reset();
                    _writeContexts.Enqueue(write);
                }
                else
                {
                    write.Dispose();
                }

                // Now that the while loop has completed the following invariants should hold true:
                Debug.Assert(_numBytesPreCompleted >= 0);
            }

            _log.ConnectionWriteCallback(_connectionId, status);
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

        private void Dispose()
        {
            lock (_lockObj)
            {
                _isDisposed = true;

                while (_writeContexts.Count > 0)
                {
                    _writeContexts.Dequeue().Dispose();
                }
            }

        }

        private class WriteContext : IDisposable
        {
            private const int BUFFER_COUNT = 4;

            public SocketOutput Self;

            public Queue<ArraySegment<byte>> Buffers;
            public bool SocketShutdownSend;
            public bool SocketDisconnect;

            public int WriteStatus;
            public Exception WriteError;

            private UvWriteReq _writeReq;
            public ArraySegment<byte>[] _segments;

            public int ShutdownSendStatus;

            public WriteContext(SocketOutput self)
            {
                Self = self;
                Buffers = new Queue<ArraySegment<byte>>(_maxPooledBufferQueues);
                _segments = new ArraySegment<byte>[BUFFER_COUNT];
                _writeReq = new UvWriteReq(Self._log);
                _writeReq.Init(Self._thread.Loop);
            }

            /// <summary>
            /// First step: initiate async write if needed, otherwise go to next step
            /// </summary>
            public void DoWriteIfNeeded()
            {
                if (Buffers.Count == 0 || Self._socket.IsClosed)
                {
                    DoShutdownIfNeeded();
                    return;
                }

                ArraySegment<byte>[] segments;
                if (Buffers.Count > BUFFER_COUNT)
                {
                    segments = new ArraySegment<byte>[Buffers.Count];
                }
                else
                {
                    segments = _segments;
                }

                var i = 0;
                foreach (var buffer in Buffers)
                {
                    segments[i++] = buffer;
                }
                
                _writeReq.Write(Self._socket, new ArraySegment<ArraySegment<byte>>(segments, 0, Buffers.Count), (_writeReq, status, error, state) =>
                {
                    var _this = (WriteContext)state;
                    _this.WriteStatus = status;
                    _this.WriteError = error;
                    _this.DoShutdownIfNeeded();
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
                Self._log.ConnectionStop(Self._connectionId);
                Complete();
            }

            public void Complete()
            {
                Self.OnWriteCompleted(this);
            }

            public void Reset()
            {
                Buffers.Clear();
                SocketDisconnect = false;
                SocketShutdownSend = false;
                WriteStatus = 0;
                WriteError = null;
                ShutdownSendStatus = 0;

                var segments = _segments;
                for (var i = 0; i < segments.Length; i++)
                {
                    segments[i] = default(ArraySegment<byte>);
                }
            }

            public void Dispose()
            {
                _writeReq.Dispose();
            }
        }
    }
}
