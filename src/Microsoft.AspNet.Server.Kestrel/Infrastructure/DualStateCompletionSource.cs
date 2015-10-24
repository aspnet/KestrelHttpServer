// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    internal sealed class DualStateCompletionSource<TState1, TState2, TResult> : INotifyCompletion, ICriticalNotifyCompletion
    {
        private readonly static Action CALLBACK_RAN = () => { };
        private bool _isCompleted;
        private Action _continuation;
        private SemaphoreSlim _semaphore;

        private Exception _error;
        private TResult _result;
        
        public DualStateCompletionSource(TState1 state1, TState2 state2)
        {
            State1 = state1;
            State2 = state2;
        }
        public TState1 State1 { get; set; }
        public TState2 State2 { get; set; }

        public bool IsCompleted { get { return _isCompleted; } }

        public void SetResult(TResult result)
        {
            _result = result;
            _isCompleted = true;

            Complete();

            Interlocked.MemoryBarrier();

            if (_semaphore != null)
            {
                _semaphore.Release();
            }
        }

        public void SetException(Exception ex)
        {
            _error = ex;
            _isCompleted = true;

            Complete();

            Interlocked.MemoryBarrier();

            if (_semaphore != null)
            {
                _semaphore.Release();
            }
        }

        private void Complete()
        {
            Action continuation = _continuation ?? Interlocked.CompareExchange(ref _continuation, CALLBACK_RAN, null);
            if (continuation != null)
            {
                CompleteCallback(continuation);
            }
        }

        private static void CompleteCallback(Action continuation)
        {
            ThreadPool.QueueUserWorkItem((obj) => RunCallback(obj), continuation);
        }

        public DualStateCompletionSource<TState1, TState2, TResult> GetAwaiter() { return this; }


        private static void RunCallback(object callback)
        {
            ((Action)callback)();
        }

        void INotifyCompletion.OnCompleted(Action continuation)
        {
            throw new NotImplementedException();
        }

        [System.Security.SecurityCritical]
        void ICriticalNotifyCompletion.UnsafeOnCompleted(Action continuation)
        {
            if (_continuation == CALLBACK_RAN ||
                    Interlocked.CompareExchange(
                        ref _continuation, continuation, null) == CALLBACK_RAN)
            {
                CompleteCallback(continuation);
            }
        }
        public TResult GetResult()
        {
            if (_error != null)
            {
                throw _error;
            }
            return _result;
        }

        public void Wait()
        {
            _semaphore = new SemaphoreSlim(0);

            Interlocked.MemoryBarrier();

            if (_isCompleted)
            {
                return;
            }

            _semaphore.Wait();
        }
    }
}
