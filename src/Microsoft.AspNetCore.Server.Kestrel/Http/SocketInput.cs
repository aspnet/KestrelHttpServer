// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    public class SocketInput : ICriticalNotifyCompletion, IDisposable
    {
        private static readonly Action _awaitableIsCompleted = () => { };
        private static readonly Action _awaitableIsNotCompleted = () => { };

        private readonly MemoryPool _memory;
        private readonly IThreadPool _threadPool;
        private readonly ManualResetEventSlim _manualResetEvent = new ManualResetEventSlim(false, 0);

        private Action _awaitableState;
        private Exception _awaitableError;

        private MemoryPoolBlock _writeHead;
        private MemoryPoolBlock _writeTail;

        private MemoryPoolBlock _readHead;
        private MemoryPoolBlock _readTail;

        private MemoryPoolBlock _socketBlock;

        private int _consumingState;
        private object _sync = new object();

        public SocketInput(MemoryPool memory, IThreadPool threadPool)
        {
            //Debugger.Launch();
            _memory = memory;
            _threadPool = threadPool;
            _awaitableState = _awaitableIsNotCompleted;
        }

        public bool RemoteIntakeFin { get; set; }

        public bool IsCompleted => ReferenceEquals(_awaitableState, _awaitableIsCompleted);

        public MemoryPoolBlock IncomingStart()
        {
            lock (_sync)
            {
                return _socketBlock ?? (_socketBlock = _memory.Lease());
            }
        }

        public void ReturnSocketBlock()
        {
            MemoryPoolBlock socketBlock;
            lock (_sync)
            {
                socketBlock = _socketBlock;
                _socketBlock = null;
            }

            socketBlock?.Pool.Return(socketBlock);
        }

        public void IncomingComplete(int count, Exception error)
        {
            const int thresholdSize = 2048;
            if (error != null)
            {
                _awaitableError = error;
            }

            if (count == 0)
            {
                ReturnSocketBlock();
                IncomingData(null, 0, 0);
                return;
            }

            if (count > thresholdSize)
            {
                lock (_sync)
                {
                    _socketBlock.End += count;
                    if (_writeHead == null)
                    {
                        _writeHead = _socketBlock;
                        _writeTail = _writeHead;
                    }
                    else
                    {
                        _writeTail.Next = _socketBlock;
                        _writeTail = _socketBlock;
                    }

                    _socketBlock = null;
                }

                return;
            }

            IncomingData(_socketBlock.Array, _socketBlock.Start, count);
        }

        public void IncomingData(byte[] buffer, int offset, int count)
        {
            lock (_sync)
            {
                if (count > 0)
                {
                    if (_writeHead == null)
                    {
                        _writeHead = _memory.Lease();
                        _writeTail = _writeHead;
                    }

                    var iterator = new MemoryPoolIterator(_writeTail, _writeTail.End);
                    iterator.CopyFrom(buffer, offset, count);

                    _writeTail = iterator.Block;
                }
                else
                {
                    RemoteIntakeFin = true;
                }

                Complete();
            }
        }

        public void IncomingFin()
        {
            // Force a FIN
            IncomingData(null, 0, 0);
        }

        private void Complete()
        {
            var awaitableState = Interlocked.Exchange(
                ref _awaitableState,
                _awaitableIsCompleted);

            _manualResetEvent.Set();

            if (!ReferenceEquals(awaitableState, _awaitableIsCompleted) &&
                !ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                _threadPool.Run(awaitableState);
            }
        }

        public MemoryPoolIterator ConsumingStart()
        {
            if (Interlocked.CompareExchange(ref _consumingState, 1, 0) != 0)
            {
                throw new InvalidOperationException("Already consuming input.");
            }

            lock (_sync)
            {
                if (_writeHead == null)
                {
                    return default(MemoryPoolIterator);
                }

                Debug.Assert(_writeHead.Start != _writeHead.End);

                _readHead = _writeHead;
                _readTail = _writeTail;
                _writeHead = null;
                _writeTail = null;
                return new MemoryPoolIterator(_readHead);
            }
        }

        public void ConsumingComplete(MemoryPoolIterator consumed, MemoryPoolIterator examined)
        {
            MemoryPoolBlock returnStart = null;
            MemoryPoolBlock returnEnd = null;

            var returnLastBlock = false;
            lock (_sync)
            {
                var newData = _writeHead != null;

                if (!consumed.IsDefault)
                {
                    var block = consumed.Block;

                    returnStart = _readHead;
                    returnEnd = block;

                    if (block == _readTail)
                    {
                        // consumed to last block
                        if (consumed.Index == block.End)
                        {
                            // consumed everything
                            returnLastBlock = true;
                        }
                        else
                        {
                            // add remaining data to write head
                            _readTail.Next = _writeHead;
                            _writeHead = block;
                            block.Start = consumed.Index;
                        }
                    }
                    else
                    {
                        // not consumed to last block
                        if (consumed.Index == block.End)
                        {
                            // consumed to end of block
                            returnLastBlock = true;
                            block = block.Next;
                        }
                        else
                        {
                            block.Start = consumed.Index;
                        }

                        // add remaining data to write head
                        _readTail.Next = _writeHead;
                        _writeHead = block;
                    }

                    if (_writeTail == null)
                    {
                        _writeTail = _readTail.Next ?? _readTail;
                    }

                    _readHead = null;
                    _readTail = null;
                }

                if (!newData &&
                    examined.IsEnd &&
                    RemoteIntakeFin == false &&
                    _awaitableError == null)
                {
                    _manualResetEvent.Reset();

                    Interlocked.CompareExchange(
                        ref _awaitableState,
                        _awaitableIsNotCompleted,
                        _awaitableIsCompleted);
                }

            }

            while (returnStart != null)
            {
                if (returnStart == returnEnd)
                {
                    if (returnLastBlock)
                    {
                        returnStart.Pool.Return(returnStart);
                    }
                    break;

                }

                var returnBlock = returnStart;
                returnStart = returnStart.Next;
                returnBlock.Pool.Return(returnBlock);
            }

            if (Interlocked.CompareExchange(ref _consumingState, 0, 1) != 1)
            {
                throw new InvalidOperationException("No ongoing consuming operation to complete.");
            }
        }

        public void CompleteAwaiting()
        {
            Complete();
        }

        public void AbortAwaiting()
        {
            _awaitableError = new TaskCanceledException("The request was aborted");

            Complete();
        }

        public SocketInput GetAwaiter()
        {
            return this;
        }

        public void OnCompleted(Action continuation)
        {
            var awaitableState = Interlocked.CompareExchange(
                ref _awaitableState,
                continuation,
                _awaitableIsNotCompleted);

            if (ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                return;
            }
            else if (ReferenceEquals(awaitableState, _awaitableIsCompleted))
            {
                _threadPool.Run(continuation);
            }
            else
            {
                _awaitableError = new InvalidOperationException("Concurrent reads are not supported.");

                Interlocked.Exchange(
                    ref _awaitableState,
                    _awaitableIsCompleted);

                _manualResetEvent.Set();

                _threadPool.Run(continuation);
                _threadPool.Run(awaitableState);
            }
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }

        public void GetResult()
        {
            if (!IsCompleted)
            {
                _manualResetEvent.Wait();
            }
            var error = _awaitableError;
            if (error != null)
            {
                if (error is TaskCanceledException || error is InvalidOperationException)
                {
                    throw error;
                }
                throw new IOException(error.Message, error);
            }
        }

        public void Dispose()
        {
            AbortAwaiting();

            // Return all blocks
            var block = _writeHead;
            while (block != null)
            {
                var returnBlock = block;
                block = block.Next;

                returnBlock.Pool.Return(returnBlock);
            }

            block = _readHead;
            while (block != null)
            {
                var returnBlock = block;
                block = block.Next;

                returnBlock.Pool.Return(returnBlock);
            }

            ReturnSocketBlock();

            _writeHead = null;
            _writeTail = null;

            _readHead = null;
            _readTail = null;
        }
    }
}
