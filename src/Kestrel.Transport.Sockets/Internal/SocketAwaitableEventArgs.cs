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

        private readonly PipeScheduler _ioScheduler;

        private Action<object> _completion;

        public SocketAwaitableEventArgs(PipeScheduler ioScheduler)
        {
            _ioScheduler = ioScheduler;
        }

        protected override void OnCompleted(SocketAsyncEventArgs _)
        {
            Action<object> completion = _completion;

            if (completion != null || (completion = Interlocked.CompareExchange(ref _completion, _callbackCompleted, null)) != null)
            {
                Debug.Assert(!ReferenceEquals(completion, _callbackCompleted));

                object state = UserToken;
                UserToken = null;

                _ioScheduler.Schedule(completion, state);
            }
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            if (ReferenceEquals(_completion, _callbackCompleted))
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
            Action<object> awaitableState = Interlocked.CompareExchange(ref _completion, continuation, null);

            if (ReferenceEquals(awaitableState, _callbackCompleted))
            {
#if NETCOREAPP2_1
                ThreadPool.QueueUserWorkItem(continuation, state, preferLocal: false);
#else
                Task.Factory.StartNew(continuation, state);
#endif
            }
            else
            {
                Debug.Fail("Multiple continuations registered!");
            }

        }

        public int GetResult(short token)
        {
            SocketError error = SocketError;
            int bytes = BytesTransferred;

            Reset();

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

            int bytesTransferred = BytesTransferred;
            SocketError error = SocketError;

            Reset();

            return error == SocketError.Success ?
                new ValueTask<int>(bytesTransferred) :
                new ValueTask<int>(Task.FromException<int>(new SocketException((int)error)));
        }

        private void Reset()
        {
            Volatile.Write(ref _completion, null);
        }

        /// <summary>Initiates a send operation on the associated socket.</summary>
        /// <returns>This instance.</returns>
        public ValueTask<int> SendAsync(Socket socket)
        {
            if (socket.SendAsync(this))
            {
                return new ValueTask<int>(this, 0);
            }

            int bytesTransferred = BytesTransferred;
            SocketError error = SocketError;

            Reset();

            return error == SocketError.Success ?
                new ValueTask<int>(bytesTransferred) :
                new ValueTask<int>(Task.FromException<int>(new SocketException((int)error)));
        }
    }
}
