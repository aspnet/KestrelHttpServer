// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class SocketAwaitableEventArgs : SocketAsyncEventArgs, IValueTaskSource<int>
    {
        private static readonly Action<object> _callbackCompleted = _ => { };
        private static readonly WaitCallback _waitCallback = OnWorkItemCallback;

        private readonly PipeScheduler _ioScheduler;

        private Action<object> _callback;

        public SocketAwaitableEventArgs(PipeScheduler ioScheduler)
        {
            _ioScheduler = ioScheduler;
        }

        protected override void OnCompleted(SocketAsyncEventArgs _)
        {
            var callback = Interlocked.CompareExchange(ref _callback, _callbackCompleted, null);

            if (callback != null)
            {
                Debug.Assert(!ReferenceEquals(callback, _callbackCompleted));

                object state = UserToken;
                UserToken = null;

                _ioScheduler.Schedule(callback, state);
            }
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            if (ReferenceEquals(_callback, _callbackCompleted))
            {
                if (SocketError != SocketError.Success)
                {
                    return ValueTaskSourceStatus.Faulted;
                }

                return ValueTaskSourceStatus.Succeeded;
            }

            return ValueTaskSourceStatus.Pending;
        }

        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            // We're ignoring ValueTaskSourceOnCompletedFlags and token since this is used in a single place in the code base
            // so we don't need to handle capturing the SynchronizationContext and ExecutionContext
            Debug.Assert((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) == 0, "FlowExecutionContext is set.");

            // Use UserToken to carry the continuation state around
            UserToken = state;
            Action<object> awaitableState = Interlocked.CompareExchange(ref _callback, continuation, null);

            if (ReferenceEquals(awaitableState, _callbackCompleted))
            {
                ThreadPool.UnsafeQueueUserWorkItem(_waitCallback, this);
            }
            else if (awaitableState != null)
            {
                throw new InvalidOperationException("Multiple continuations registered!");
            }

        }

        public int GetResult(short token)
        {
            SocketError error = SocketError;
            int bytes = BytesTransferred;

            Volatile.Write(ref _callback, null);

            if (error != SocketError.Success)
            {
                ThrowSocketException(error);
            }

            return bytes;

            void ThrowSocketException(SocketError e)
            {
                throw new SocketException((int)e);
            }
        }

        public ValueTask<int> ReceiveAsync(Socket socket)
        {
            if (socket.ReceiveAsync(this))
            {
                return new ValueTask<int>(this, 0);
            }

            return GetCompletedValueTask();
        }

        /// <summary>Initiates a send operation on the associated socket.</summary>
        /// <returns>This instance.</returns>
        public ValueTask<int> SendAsync(Socket socket)
        {
            if (socket.SendAsync(this))
            {
                return new ValueTask<int>(this, 0);
            }

            return GetCompletedValueTask();
        }

        private ValueTask<int> GetCompletedValueTask()
        {
            int bytesTransferred = BytesTransferred;
            SocketError error = SocketError;

            return error == SocketError.Success ?
                new ValueTask<int>(bytesTransferred) :
                new ValueTask<int>(Task.FromException<int>(new SocketException((int)error)));
        }

        private static void OnWorkItemCallback(object state)
        {
            var args = (SocketAwaitableEventArgs)state;
            var callbackState = args.UserToken;
            args.UserToken = null;
            var callback = args._callback;
            callback(callbackState);
        }
    }
}
