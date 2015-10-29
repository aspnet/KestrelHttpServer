// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.AspNet.Server.Kestrel.Networking;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class SocketOutput : ISocketOutput
    {
        internal const int MaxBytesPreCompleted = 3 * (MemoryPool2.NativeBlockSize * UvWriteReq.BUFFER_COUNT) - 1;

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
        private readonly Queue<CallbackContext> _callbacksPending;

        public int ShutdownSendStatus;

        private int pendingWriteBitFlag = 0;
        private bool SocketDisconnected;
        private bool SocketShutdownSent;

        public SocketOutput(MemoryPool2 memory, KestrelThread thread, UvStreamHandle socket, long connectionId, IKestrelTrace log)
        {
            _thread = thread;
            _socket = socket;
            _connectionId = connectionId;
            _log = log;
            _callbacksPending = new Queue<CallbackContext>();
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
                        memoryBlock = _memory.Lease(MemoryPool2.NativeBlockSize);
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
                    }
                }
                
                Interlocked.Exchange(ref _currentWriteBlock.Block, memoryBlock);

                if (immediate)
                {
                    _currentWriteBlock.SocketDisconnect |= socketDisconnect;
                    _currentWriteBlock.SocketShutdownSend |= socketShutdownSend;
                }
            }

            if (immediate)
            {
                SendBufferedData();
            }

            lock (_callbacksPending)
            {
                if (_lastWriteError == null &&
                    _callbacksPending.Count == 0 &&
                    inputLength + _numBytesPreCompleted <= MaxBytesPreCompleted)
                {
                    triggerCallbackNow = true;
                }
                else
                {
                    triggerCallbackNow = false;
                }
            }

            if (triggerCallbackNow)
            {
                callback(null, state, 0, true);
            }
            else
            {
                lock (_callbacksPending)
                {
                    _callbacksPending.Enqueue(new CallbackContext
                    {
                        Callback = callback,
                        State = state,
                        BytesWrittenThreshold = queuedBytes
                    });
                }
            }

            Interlocked.Add(ref _numBytesPreCompleted, inputLength);
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
                var block = Interlocked.Exchange(ref _currentWriteBlock.Block, null); ;

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
                else
                {
                    socketDisconnect = _currentWriteBlock.SocketDisconnect;
                    socketShutdownSend = _currentWriteBlock.SocketShutdownSend;
                }
            }

            if (count == 0 && !socketDisconnect && !socketShutdownSend)
            {
                return null;
            }

            Interlocked.Add(ref _bytesQueued, dataLength);

            return new WriteContext()
            {
                Data = new ArraySegment<MemoryPoolBlock2>(data ?? _emptyBlocks, 0, count),
                Output = this,
                SocketDisconnect = socketDisconnect,
                SocketShutdownSend = socketShutdownSend
            };
        }

        private void SendBufferedData()
        {
            if (Interlocked.CompareExchange(ref pendingWriteBitFlag, 1, 0) == 0)
            {
                _thread.Post(so => WriteContext.DoWriteIfNeeded(so), this);
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
            _log.ConnectionWriteCallback(_connectionId, status);

            Monitor.Enter(_callbacksPending);
            var hasLock = true;
            try
            {
                if (_callbacksPending.Count == 0)
                {
                    return;
                }

                var hasAvailableCallback = _callbacksPending.Peek().BytesWrittenThreshold <= _bytesWritten;

                _lastWriteError = error;
                while (hasAvailableCallback)
                {
                    var callbackContext = _callbacksPending.Dequeue();

                    Monitor.Exit(_callbacksPending);
                    hasLock = false;

                    // callback(error, state, calledInline)
                    callbackContext.Callback(error, callbackContext.State, status, false);

                    hasAvailableCallback = _callbacksPending.Count > 0 &&
                       _callbacksPending.Peek().BytesWrittenThreshold <= _bytesWritten;

                    if (hasAvailableCallback)
                    {
                        Monitor.Enter(_callbacksPending);
                        hasLock = true;
                    }
                }

            }
            finally
            {
                if (hasLock)
                {
                    Monitor.Exit(_callbacksPending);
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
                (error, state, status, calledInline) =>
                {
                    if (status < 0)
                    {
                        if (!calledInline)
                        {
                            ThreadPool.QueueUserWorkItem((state2) =>
                            {
                                var tcs2 = (TaskCompletionSource<int>)state2;
                                if (error != null)
                                {
                                    tcs2.SetException(error);
                                }
                                else
                                {
                                    tcs2.SetResult(0);
                                }
                            }, tcs);
                        }
                        else
                        {
                            if (error != null)
                            {
                                tcs.SetException(error);
                            }
                            else
                            {
                                tcs.SetResult(0);
                            }
                        }
                    }
                    else
                    {
                        if (!calledInline)
                        {
                            ThreadPool.QueueUserWorkItem((state2) =>
                            {
                                var tcs2 = (TaskCompletionSource<int>)state2;
                                tcs.SetResult(0);
                            }, tcs);
                        }
                        else
                        {
                            tcs.SetResult(0);
                        }
                    }
                },
                tcs,
                immediate: true);

            return tcs.Task;
        }

        private class WriteContext
        {
            public ArraySegment<MemoryPoolBlock2> Data;
            public SocketOutput Output;
            public bool SocketShutdownSend;
            public bool SocketDisconnect;

            private int Status;
            private Exception Error;

            /// <summary>
            /// First step: initiate async write if needed, otherwise go to next step
            /// </summary>
            public static void DoWriteIfNeeded(SocketOutput socketOutput)
            {
                socketOutput.pendingWriteBitFlag = 0;
                Interlocked.MemoryBarrier();

                WriteContext context;
                while ((context = socketOutput.GetContext()) != null)
                {
                    var data = context.Data;

                    if (data.Count == 0 || socketOutput._socket.IsClosed)
                    {
                        DoShutdownIfNeeded(context);
                        return;
                    }
                    var writeReq = new UvWriteReq(socketOutput._log);
                    writeReq.Init(socketOutput._thread.Loop);
                    writeReq.Write(socketOutput._socket,
                        data,
                        (_writeReq, status, error, bytesWritten, state) => WriteCallback(_writeReq, status, error, bytesWritten, state),
                        context);
                }
            }

            private static void WriteCallback(UvWriteReq writeReq, int status, Exception error, int bytesWritten, object state)
            {
                writeReq.Dispose();
                var _this = (WriteContext)state;
                var socketOutput = _this.Output;

                Interlocked.Add(ref socketOutput._bytesWritten, bytesWritten);
                Interlocked.Add(ref socketOutput._numBytesPreCompleted, -bytesWritten);

                _this.Error = error;
                _this.Status = status;

                DoShutdownIfNeeded(_this);
            }

            /// <summary>
            /// Second step: initiate async shutdown if needed, otherwise go to next step
            /// </summary>
            private static void DoShutdownIfNeeded(WriteContext req)
            {
                var socketOutput = req.Output;
                if (socketOutput.SocketShutdownSent || req.SocketShutdownSend == false || socketOutput._socket.IsClosed)
                {
                    DoDisconnectIfNeeded(req);
                    return;
                }

                socketOutput.SocketShutdownSent = true;
                var shutdownReq = new UvShutdownReq(req.Output._log);
                shutdownReq.Init(socketOutput._thread.Loop);
                shutdownReq.Shutdown(socketOutput._socket,
                    (shutdownReq2, status2, state) => ShutdownCallback(shutdownReq2, status2, state), req);
            }

            private static void ShutdownCallback(UvShutdownReq shutdownReq, int status, object state)
            {
                shutdownReq.Dispose();
                var _this = (WriteContext)state;
                var socketOutput = _this.Output;
                socketOutput.ShutdownSendStatus = status;

                socketOutput._log.ConnectionWroteFin(socketOutput._connectionId, status);

                DoDisconnectIfNeeded(_this);
            }

            /// <summary>
            /// Third step: disconnect socket if needed, otherwise this work item is complete
            /// </summary>
            private static void DoDisconnectIfNeeded(WriteContext req)
            {
                var socketOutput = req.Output;
                if (socketOutput.SocketDisconnected || req.SocketDisconnect == false || socketOutput._socket.IsClosed)
                {
                    socketOutput.OnWriteCompleted(req.Status, req.Error);
                    return;
                }

                socketOutput.SocketDisconnected = true;
                socketOutput._socket.Dispose();
                socketOutput._log.ConnectionStop(socketOutput._connectionId);
                socketOutput.OnWriteCompleted(req.Status, req.Error);
            }
        }

        private struct CallbackContext
        {
            // callback(error, state, calledInline)
            public Action<Exception, object, int, bool> Callback;
            public object State;
            public long BytesWrittenThreshold;
        }

        private struct WriteBlock
        {
            public MemoryPoolBlock2 Block;
            public bool SocketShutdownSend;
            public bool SocketDisconnect;
        }
    }
}
