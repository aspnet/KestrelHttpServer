// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class SocketInput : ICriticalNotifyCompletion, IDisposable
    {
        private static readonly Action _awaitableIsCompleted = () => { };
        private static readonly Action _awaitableIsNotCompleted = () => { };

        private readonly MemoryPool _memory;
        private readonly IThreadPool _threadPool;
        private readonly IBufferSizeControl _bufferSizeControl;
        private readonly ManualResetEventSlim _manualResetEvent = new ManualResetEventSlim(false, 0);

        private Action _awaitableState;

        private MemoryPoolBlock _head;
        private MemoryPoolBlock _tail;
        private MemoryPoolBlock _pinned;

        private object _sync = new object();

        private bool _consuming;
        private bool _disposed;

        private TaskCompletionSource<object> _tcs = new TaskCompletionSource<object>();

        public SocketInput(MemoryPool memory, IThreadPool threadPool, IBufferSizeControl bufferSizeControl = null)
        {
            _memory = memory;
            _threadPool = threadPool;
            _bufferSizeControl = bufferSizeControl;
            _awaitableState = _awaitableIsNotCompleted;
        }

        public bool IsCompleted => ReferenceEquals(_awaitableState, _awaitableIsCompleted);

        private bool ReadingInput => _tcs.Task.Status == TaskStatus.WaitingForActivation;

        public bool CheckFinOrThrow()
        {
            CheckConnectionError();
            return _tcs.Task.Status == TaskStatus.RanToCompletion;
        }

        public MemoryPoolBlock IncomingStart()
        {
            lock (_sync)
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
        }

        public void IncomingComplete(int count, Exception error)
        {
            Action awaitableState;

            lock (_sync)
            {
                // Must call Add() before bytes are available to consumer, to ensure that Length is >= 0
                _bufferSizeControl?.Add(count);

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
                        Volatile.Write(ref _tail.Next, _pinned);
                        _tail = _pinned;
                    }

                    _pinned = null;
                }

                if (error != null)
                {
                    SetConnectionError(error);
                }
                else if (count == 0)
                {
                    FinReceived();
                }

                awaitableState = Interlocked.Exchange(ref _awaitableState, _awaitableIsCompleted);
            }

            Complete(awaitableState);
        }

        public void IncomingDeferred()
        {
            Debug.Assert(_pinned != null);

            lock (_sync)
            {
                if (_pinned != null)
                {
                    if (_pinned != _tail)
                    {
                        _memory.Return(_pinned);
                    }

                    _pinned = null;
                }
            }
        }

        private void Complete(Action awaitableState)
        {
            _manualResetEvent.Set();

            if (!ReferenceEquals(awaitableState, _awaitableIsCompleted) &&
                !ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                _threadPool.Run(awaitableState);
            }
        }

        public MemoryPoolIterator ConsumingStart()
        {
            MemoryPoolBlock head;
            bool isAlreadyConsuming;

            lock (_sync)
            {
                isAlreadyConsuming = _consuming;
                head = _head;
                _consuming = true;
            }

            if (isAlreadyConsuming)
            {
                throw new InvalidOperationException("Already consuming input.");
            }

            return new MemoryPoolIterator(head);
        }

        public void ConsumingComplete(
            MemoryPoolIterator consumed,
            MemoryPoolIterator examined)
        {
            bool isConsuming;
            MemoryPoolBlock returnStart = null;
            MemoryPoolBlock returnEnd = null;

            lock (_sync)
            {
                if (!_disposed)
                {
                    if (!consumed.IsDefault)
                    {
                        // Compute lengthConsumed before modifying _head or consumed
                        var lengthConsumed = 0;
                        if (_bufferSizeControl != null)
                        {
                            lengthConsumed = new MemoryPoolIterator(_head).GetLength(consumed);
                        }

                        returnStart = _head;

                        var consumedAll = !consumed.IsDefault && consumed.IsEnd;
                        if (consumedAll && _pinned != _tail)
                        {
                            // Everything has been consumed and no data is being written to the
                            // _tail block, so return all blocks between _head and _tail inclusive.
                            _head = null;
                            _tail = null;
                        }
                        else
                        {
                            returnEnd = consumed.Block;
                            _head = consumed.Block;
                            _head.Start = consumed.Index;
                        }

                        // Must call Subtract() after _head has been advanced, to avoid producer starting too early and growing
                        // buffer beyond max length.
                        _bufferSizeControl?.Subtract(lengthConsumed);
                    }

                    // If _head is null, everything has been consumed and examined.
                    var examinedAll = (!examined.IsDefault && examined.IsEnd) || _head == null;
                    if (examinedAll && ReadingInput)
                    {
                        _manualResetEvent.Reset();

                        Interlocked.CompareExchange(
                            ref _awaitableState,
                            _awaitableIsNotCompleted,
                            _awaitableIsCompleted);
                    }
                }
                else
                {
                    // Dispose won't have returned the blocks if we were consuming, so return them now
                    returnStart = _head;
                    _head = null;
                    _tail = null;
                }

                isConsuming = _consuming;
                _consuming = false;
            }

            ReturnBlocks(returnStart, returnEnd);

            if (!isConsuming)
            {
                throw new InvalidOperationException("No ongoing consuming operation to complete.");
            }
        }

        public void CompleteAwaiting()
        {
            Complete(Interlocked.Exchange(ref _awaitableState, _awaitableIsCompleted));
        }

        public void AbortAwaiting()
        {
            SetConnectionError(new TaskCanceledException("The request was aborted"));

            CompleteAwaiting();
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

            if (ReferenceEquals(awaitableState, _awaitableIsCompleted))
            {
                _threadPool.Run(continuation);
            }
            else if (!ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                SetConnectionError(new InvalidOperationException("Concurrent reads are not supported."));

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

            CheckConnectionError();
        }

        public void Dispose()
        {
            AbortAwaiting();

            MemoryPoolBlock block = null;

            lock (_sync)
            {
                if (!_consuming)
                {
                    block = _head;
                    _head = null;
                    _tail = null;
                }

                _disposed = true;
            }

            ReturnBlocks(block, null);
        }

        private static void ReturnBlocks(MemoryPoolBlock block, MemoryPoolBlock end)
        {
            while (block != end)
            {
                var returnBlock = block;
                block = block.Next;

                returnBlock.Pool.Return(returnBlock);
            }
        }

        private void SetConnectionError(Exception error)
        {
            _tcs.TrySetException(error);
            // Prevent UnobservedTaskException
            var ignore = _tcs.Task.Exception;
        }

        private void FinReceived()
        {
            _tcs.TrySetResult(null);
        }

        private void CheckConnectionError()
        {
            var error = _tcs.Task.Exception?.InnerException;
            if (error != null)
            {
                throw error;
            }
        }
    }
}
