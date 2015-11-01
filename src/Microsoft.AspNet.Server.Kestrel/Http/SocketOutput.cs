// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.AspNet.Server.Kestrel.Networking;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class SocketOutput : ISocketOutput
    {
        // ~64k; act=64512
        internal const int MaxBytesPreCompleted = 2 * (MemoryPool2.WritableBlockSize * UvWriteReq.BUFFER_COUNT) - 1;

        private static MemoryPoolBlock2[] _emptyBlocks = new MemoryPoolBlock2[0];

        private readonly KestrelThread _thread;
        private readonly UvStreamHandle _socket;
        private readonly long _connectionId;
        private readonly IKestrelTrace _log;

        private readonly MemoryPool2 _memory;

        private WriteBlock _currentWriteBlock;
        private ConcurrentQueue<WriteBlock> _memoryBlocks;

        private long _bytesQueued;
        private long _bytesWritten;

        private int _numBytesPreCompleted = 0;
        private Exception _lastWriteError;
        private int _lastStatus = 0;
        private readonly ConcurrentQueue<CallbackContext> _callbacksPending;

        public int ShutdownSendStatus;

        private volatile int pendingWriteBitFlag = 0;
        private bool SocketShutdownSent;

        public SocketOutput(MemoryPool2 memory, KestrelThread thread, UvStreamHandle socket, long connectionId, IKestrelTrace log)
        {
            _thread = thread;
            _socket = socket;
            _connectionId = connectionId;
            _log = log;
            _callbacksPending = new ConcurrentQueue<CallbackContext>();
            _memory = memory;
            _memoryBlocks = new ConcurrentQueue<WriteBlock>();
        }

        internal void Write(
            ArraySegment<byte> buffer,
            Action<Exception, object, int, bool> callback,
            object state,
            bool immediate = true,
            bool socketShutdownSend = false,
            bool socketDisconnect = false)
        {
            bool triggerCallbackNow;
            var queuedBytes = _bytesQueued;
            bool blockFilled = false;

            var inputLength = buffer.Array != null ? buffer.Count : 0;
            MemoryPoolBlock2 memoryBlock;

            if (inputLength > 0)
            {
                memoryBlock = Interlocked.Exchange(ref _currentWriteBlock.Block, null);

                _log.ConnectionWrite(_connectionId, inputLength);

                int blockRemaining = memoryBlock != null ? memoryBlock.Data.Count - (memoryBlock.End - memoryBlock.Start) : 0;

                var remaining = inputLength;
                var offset = buffer.Offset;

                while (remaining > 0)
                {
                    if (memoryBlock == null)
                    {
                        memoryBlock = _memory.Lease(MemoryPool2.WritableBlockSize);
                        blockRemaining = memoryBlock.Data.Count;
                    }

                    var copyAmount = blockRemaining >= remaining ? remaining : blockRemaining;
                    Buffer.BlockCopy(buffer.Array, offset, memoryBlock.Array, memoryBlock.End, copyAmount);

                    remaining -= copyAmount;
                    blockRemaining -= copyAmount;
                    memoryBlock.End += copyAmount;
                    offset += copyAmount;

                    if (blockRemaining == 0)
                    {
                        _memoryBlocks.Enqueue(new WriteBlock() { Block = memoryBlock });
                        memoryBlock = null;
                        blockFilled = true;
                    }
                }

                Interlocked.Exchange(ref _currentWriteBlock.Block, memoryBlock);
            }
            
            CallbackContext callbackContext;
            var nextPendingBytes = inputLength + _numBytesPreCompleted;
            if (_lastWriteError == null &&
                _callbacksPending.TryPeek(out callbackContext) &&
                nextPendingBytes <= MaxBytesPreCompleted)
            {
                triggerCallbackNow = true;
            }
            else
            {
                triggerCallbackNow = queuedBytes <= _bytesWritten;
            }

            if (!triggerCallbackNow)
            {
                callbackContext = new CallbackContext
                {
                    Callback = callback,
                    State = state,
                    BytesWrittenThreshold = queuedBytes
                };
                _callbacksPending.Enqueue(callbackContext);
            }

            if (immediate)
            {
                _currentWriteBlock.SocketDisconnect |= socketDisconnect;
                _currentWriteBlock.SocketShutdownSend |= socketShutdownSend;
            }
            if (immediate || blockFilled)
            {
                SendBufferedData();
            }

            if (triggerCallbackNow)
            {
                callback(null, state, 0, true);
            }

            Interlocked.Add(ref _numBytesPreCompleted, inputLength);
        }

        private void SendBufferedData()
        {
            if (Interlocked.CompareExchange(ref pendingWriteBitFlag, 1, 0) == 0)
            {
                _thread.Post(so => so.DoWriteIfNeeded(), this);
            }
        }

        public void End(ProduceEndType endType)
        {
            switch (endType)
            {
                case ProduceEndType.SocketShutdownSend:
                    Write(default(ArraySegment<byte>), (error, state, status, calledInline) => { }, null,
                        immediate: true,
                        socketShutdownSend: true,
                        socketDisconnect: false);
                    break;
                case ProduceEndType.SocketDisconnect:
                    Write(default(ArraySegment<byte>), (error, state, status, calledInline) => { }, null,
                        immediate: true,
                        socketShutdownSend: false,
                        socketDisconnect: true);
                    break;
            }
        }

        // This is called on the libuv event loop
        private void OnWriteCompleted(int status, Exception error)
        {
            _lastWriteError = error;
            _lastStatus = status;

            CallbackContext callbackContext;
            while (_callbacksPending.TryPeek(out callbackContext) && callbackContext.BytesWrittenThreshold <= _bytesWritten)
            {
                _log.ConnectionWriteCallback(_connectionId, status);

                if (_callbacksPending.TryDequeue(out callbackContext))
                {
                    //Callback(error, state, calledInline)
                    callbackContext.Callback(error, callbackContext.State, status, false);
                }
            }
        }

        void ISocketOutput.Write(ArraySegment<byte> buffer, bool immediate)
        {
            if (!immediate)
            {
                // immediate==false calls always return complete tasks, because there is guaranteed
                // to be a subsequent immediate==true call which will go down the following code-path
                Write(
                    buffer,
                    (error, state, status, calledInline) => { },
                    null,
                    immediate: false);

                return;
            }

            // TODO: Optimize task being used, and remove callback model from the underlying Write
            var tcs = new TaskCompletionSource<int>();

            Write(
                buffer,
                (error, state, status, calledInline) =>
                {
                    var cs = (TaskCompletionSource<int>)state;
                    if (error != null)
                    {
                        cs.SetException(error);
                    }
                    else
                    {
                        cs.SetResult(0);
                    }
                },
                tcs,
                immediate: true);

            if (tcs.Task.Status != TaskStatus.RanToCompletion)
            {
                tcs.Task.GetAwaiter().GetResult();
            }
        }

        Task ISocketOutput.WriteAsync(ArraySegment<byte> buffer, bool immediate, CancellationToken cancellationToken)
        {
            if (!immediate)
            {
                // immediate==false calls always return complete tasks, because there is guaranteed
                // to be a subsequent immediate==true call which will go down the following code-path
                Write(
                    buffer,
                    (error, state, status, calledInline) => { },
                    null,
                    immediate: false);

                return TaskUtilities.CompletedTask;
            }

            // TODO: Optimize task being used, and remove callback model from the underlying Write
            var tcs = new TaskCompletionSource<int>();

            Write(
                buffer,
                (error, state2, status, calledInline) =>
                {
                    var tcs2 = (TaskCompletionSource<int>)state2;
                    if (status < 0)
                    {
                        if (!calledInline)
                        {
                            ThreadPool.QueueUserWorkItem((state3) =>
                            {
                                var tcs3 = (TaskCompletionSource<int>)state3;
                                if (error != null)
                                {
                                    tcs3.SetException(error);
                                }
                                else
                                {
                                    tcs3.SetResult(0);
                                }
                            }, tcs2);
                        }
                        else
                        {
                            if (error != null)
                            {
                                tcs2.SetException(error);
                            }
                            else
                            {
                                tcs2.SetResult(0);
                            }
                        }
                    }
                    else
                    {
                        if (!calledInline)
                        {
                            ThreadPool.QueueUserWorkItem((state3) =>
                            {
                                var tcs3 = (TaskCompletionSource<int>)state3;
                                tcs3.SetResult(0);
                            }, tcs2);
                        }
                        else
                        {
                            tcs2.SetResult(0);
                        }
                    }
                },
                tcs,
                immediate: true);

            return tcs.Task;
        }

        private WriteContext GetContext()
        {
            MemoryPoolBlock2[] data = null;

            var count = 0;
            var dataLength = 0;

            bool socketDisconnect = false;
            bool socketShutdownSend = false;

            WriteBlock writeBlock;
            while (_memoryBlocks.TryDequeue(out writeBlock))
            {
                var block = writeBlock.Block;
                if (block != null)
                {
                    if (data == null)
                    {
                        data = new MemoryPoolBlock2[UvWriteReq.BUFFER_COUNT];
                    }
                    var length = block.End - block.Start;
                    data[count] = block;
                    dataLength += length;
                    count++;
                }

                socketDisconnect |= writeBlock.SocketDisconnect;
                socketShutdownSend |= writeBlock.SocketShutdownSend;

                if (count == UvWriteReq.BUFFER_COUNT)
                {
                    break;
                }
            }

            if (count < UvWriteReq.BUFFER_COUNT)
            {
                var block = Interlocked.Exchange(ref _currentWriteBlock.Block, null);

                if (block != null)
                {
                    if (data == null)
                    {
                        data = new MemoryPoolBlock2[UvWriteReq.BUFFER_COUNT];
                    }
                    var length = block.End - block.Start;
                    data[count] = block;
                    dataLength += length;
                    count++;
                }
                socketDisconnect |= _currentWriteBlock.SocketDisconnect;
                socketShutdownSend |= _currentWriteBlock.SocketShutdownSend;
            }

            Interlocked.Add(ref _bytesQueued, dataLength);

            return new WriteContext()
            {
                Data = new ArraySegment<MemoryPoolBlock2>(data ?? _emptyBlocks, 0, count),
                SocketDisconnect = socketDisconnect,
                SocketShutdownSend = socketShutdownSend
            };
        }

        /// <summary>
        /// First step: initiate async write if needed, otherwise go to next step
        /// </summary>
        public void DoWriteIfNeeded()
        {
            pendingWriteBitFlag = 0;
            Interlocked.MemoryBarrier();

            WriteContext context;
            while (true)
            {
                context = GetContext();
                var data = context.Data;

                if (data.Count == 0 || _socket.IsClosed)
                {
                    DoShutdownIfNeeded(context.SocketDisconnect, context.SocketShutdownSend, 0, null);
                    return;
                }

                var writeReq = _thread.LeaseWriteRequest();

                writeReq.SocketDisconnect = context.SocketDisconnect;
                writeReq.SocketShutdownSend = context.SocketShutdownSend;
                writeReq.Write(_socket,
                    data,
                    (_writeReq, status, error, bytesWritten, state) => WriteCallback(_writeReq, status, error, bytesWritten, state),
                    this);
            }
        }

        private static void WriteCallback(UvWriteReq writeReq, int status, Exception error, int bytesWritten, object state)
        {
            var socketOutput = (SocketOutput)state;
            socketOutput._thread.ReturnWriteRequest(writeReq);

            Interlocked.Add(ref socketOutput._bytesWritten, bytesWritten);
            Interlocked.Add(ref socketOutput._numBytesPreCompleted, -bytesWritten);

            socketOutput.DoShutdownIfNeeded(writeReq.SocketDisconnect, writeReq.SocketShutdownSend, status, error);
        }

        /// <summary>
        /// Second step: initiate async shutdown if needed, otherwise go to next step
        /// </summary>
        private void DoShutdownIfNeeded(bool socketDisconnect, bool socketShutdownSend, int status, Exception error)
        {
            if (socketShutdownSend == false || SocketShutdownSent == true || _socket.IsClosed)
            {
                DoDisconnectIfNeeded(socketDisconnect, status, error);
                return;
            }
            SocketShutdownSent = true;
            
            var shutdownReq = new UvShutdownReq(_log);
            shutdownReq.Init(_thread.Loop);

            shutdownReq.SocketDisconnect = socketDisconnect;
            shutdownReq.SocketStatus = status;
            shutdownReq.SocketException = error;

            shutdownReq.Shutdown(_socket,
                (shutdownReq2, status2, state) => ShutdownCallback(shutdownReq2, status2, state), this);
        }

        private static void ShutdownCallback(UvShutdownReq shutdownReq, int status, object state)
        {
            shutdownReq.Dispose();
            var socketOutput = (SocketOutput)state;
            socketOutput.ShutdownSendStatus = status;

            socketOutput._log.ConnectionWroteFin(socketOutput._connectionId, status);

            socketOutput.DoDisconnectIfNeeded(shutdownReq.SocketDisconnect, shutdownReq.SocketStatus, shutdownReq.SocketException);
        }

        /// <summary>
        /// Third step: disconnect socket if needed, otherwise this work item is complete
        /// </summary>
        private void DoDisconnectIfNeeded(bool socketDisconnect, int status, Exception error)
        {
            if (socketDisconnect == false || _socket.IsClosed)
            {
                OnWriteCompleted(status, error);
                return;
            }

            _socket.Dispose();
            _log.ConnectionStop(_connectionId);
            OnWriteCompleted(status, error);
        }

        private struct CallbackContext
        {
            //Callback(error, state, calledInline)
            public Action<Exception, object, int, bool> Callback;
            public object State;
            public long BytesWrittenThreshold;
        }

        private struct WriteContext
        {
            public ArraySegment<MemoryPoolBlock2> Data;
            public bool SocketShutdownSend;
            public bool SocketDisconnect;
        }

        private struct WriteBlock
        {
            public MemoryPoolBlock2 Block;
            public bool SocketShutdownSend;
            public bool SocketDisconnect;
        }
    }
}
