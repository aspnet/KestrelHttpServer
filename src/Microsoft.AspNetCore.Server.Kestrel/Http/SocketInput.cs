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

        private MemoryPoolBlock _head;
        private MemoryPoolBlock _tail;
        private MemoryPoolBlock _pinned;

        private int _consumingState;
        private object _sync = new object();

        public SocketInput(MemoryPool memory, IThreadPool threadPool)
        {
            _memory = memory;
            _threadPool = threadPool;
            _awaitableState = _awaitableIsNotCompleted;
        }

        public bool RemoteIntakeFin { get; set; }

        public bool IsCompleted => ReferenceEquals(_awaitableState, _awaitableIsCompleted);

        public MemoryPoolBlock IncomingStart()
        {
            const int minimumSize = 2048;

            if (_tail != null && minimumSize <= _tail.Data.Offset + _tail.Data.Count - _tail.End)
            {
                _pinned = _tail;
            }
            else
            {
                _pinned = _memory.Lease();
            }

            return _pinned;
        }

        public void IncomingData(byte[] buffer, int offset, int count)
        {
            Action awaitableState;
            lock (_sync)
            {
                if (count > 0)
                {
                    if (_tail == null)
                    {
                        _tail = _memory.Lease();
                    }

                    var iterator = new MemoryPoolIterator(_tail, _tail.End);
                    iterator.CopyFrom(buffer, offset, count);

                    if (_head == null)
                    {
                        _head = _tail;
                    }

                    _tail = iterator.Block;
                }
                else
                {
                    RemoteIntakeFin = true;
                }

                awaitableState = Complete();
            }

            if (awaitableState != null)
            {
                _threadPool.Run(awaitableState);
            }
        }

        public void IncomingComplete(int count, Exception error)
        {
            Action awaitableState;
            lock (_sync)
            {
                if (_pinned != null)
                {
                    _pinned.End += count;

                    if (_head == null)
                    {
                        _head = _tail = _pinned;
                    }
                    else if (_tail == _pinned)
                    {
                        // NO-OP: this was a read into unoccupied tail-space
                    }
                    else
                    {
                        _tail.Next = _pinned;
                        _tail = _pinned;
                    }

                    _pinned = null;
                }

                if (count == 0)
                {
                    RemoteIntakeFin = true;
                }
                if (error != null)
                {
                    _awaitableError = error;
                }

                awaitableState = Complete();
            }

            if (awaitableState != null)
            {
                _threadPool.Run(awaitableState);
            }
        }

        public void IncomingDeferred()
        {
            Debug.Assert(_pinned != null);

            if (_pinned != null)
            {
                if (_pinned != _tail)
                {
                    _memory.Return(_pinned);
                }

                _pinned = null;
            }
        }

        public void IncomingFin()
        {
            // Force a FIN
            IncomingData(null, 0, 0);
        }

        private Action Complete()
        {
            var awaitableState = Interlocked.Exchange(
                ref _awaitableState,
                _awaitableIsCompleted);

            _manualResetEvent.Set();

            if (!ReferenceEquals(awaitableState, _awaitableIsCompleted) &&
                !ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                return awaitableState;
            }

            return null;
        }

        public MemoryPoolIterator ConsumingStart()
        {
            if (Interlocked.CompareExchange(ref _consumingState, 1, 0) != 0)
            {
                throw new InvalidOperationException("Already consuming input.");
            }

            return new MemoryPoolIterator(_head);
        }

        public void ConsumingComplete(
            MemoryPoolIterator consumed,
            MemoryPoolIterator examined)
        {
            MemoryPoolBlock returnStart = null;
            MemoryPoolBlock returnEnd = null;

            lock (_sync)
            {
                if (!consumed.IsDefault)
                {
                    returnStart = _head;
                    returnEnd = consumed.Block;
                    _head = consumed.Block;
                    _head.Start = consumed.Index;
                }

                if (!examined.IsEnd &&
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

            while (returnStart != returnEnd)
            {
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
            var awaitableState = Complete();
            if (awaitableState != null)
            {
                _threadPool.Run(awaitableState);
            }
        }

        public void AbortAwaiting()
        {
            _awaitableError = new TaskCanceledException("The request was aborted");

            var awaitableState = Complete();
            if (awaitableState != null)
            {
                _threadPool.Run(awaitableState);
            }
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
            var block = _head;
            while (block != null)
            {
                var returnBlock = block;
                block = block.Next;

                returnBlock.Pool.Return(returnBlock);
            }

            _head = null;
            _tail = null;
        }
    }
}
