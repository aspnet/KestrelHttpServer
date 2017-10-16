// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    internal struct PipeAwaitable
    {
        private static readonly Action _awaitableIsCompleted = () => { };
        private static readonly Action _awaitableIsNotCompleted = () => { };

        private CancelledState _cancelledState;
        private Action _state;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationTokenRegistration;

        public PipeAwaitable(bool completed)
        {
            _cancelledState = CancelledState.NotCancelled;
            _state = completed ? _awaitableIsCompleted : _awaitableIsNotCompleted;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CancellationTokenRegistration AttachToken(CancellationToken cancellationToken, Action<object> callback, object state)
        {
            CancellationTokenRegistration oldRegistration;
            if (!cancellationToken.Equals(_cancellationToken))
            {
                oldRegistration = _cancellationTokenRegistration;
                _cancellationToken = cancellationToken;
                if (_cancellationToken.CanBeCanceled)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    _cancellationTokenRegistration = _cancellationToken.Register(callback, state);
                }
            }
            return oldRegistration;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Action Complete()
        {
            var awaitableState = _state;
            _state = _awaitableIsCompleted;

            if (!ReferenceEquals(awaitableState, _awaitableIsCompleted) &&
                !ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                return awaitableState;
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            if (ReferenceEquals(_state, _awaitableIsCompleted) &&
                _cancelledState < CancelledState.CancellationPreRequested)
            {
                _state = _awaitableIsNotCompleted;
            }

            // Change the state from observed -> not cancelled.
            // We only want to reset the cancelled state if it was observed
            if (_cancelledState == CancelledState.CancellationObserved)
            {
                _cancelledState = CancelledState.NotCancelled;
            }
        }

        public bool IsCompleted => ReferenceEquals(_state, _awaitableIsCompleted);
        internal bool HasContinuation => !ReferenceEquals(_state, _awaitableIsNotCompleted);

        public Action OnCompleted(Action continuation, out bool doubleCompletion)
        {
            doubleCompletion = false;
            var awaitableState = _state;
            if (ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                _state = continuation;
            }

            if (ReferenceEquals(awaitableState, _awaitableIsCompleted))
            {
                return continuation;
            }

            if (!ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                doubleCompletion = true;
                return continuation;
            }

            return null;
        }

        public Action Cancel()
        {
            var action = Complete();
            _cancelledState = action == null ?
                CancelledState.CancellationPreRequested :
                CancelledState.CancellationRequested;
            return action;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ObserveCancelation()
        {
            if (_cancelledState == CancelledState.NotCancelled)
            {
                return false;
            }

            bool isPrerequested = _cancelledState == CancelledState.CancellationPreRequested;

            if (_cancelledState >= CancelledState.CancellationPreRequested)
            {
                _cancelledState = CancelledState.CancellationObserved;

                // Do not reset awaitable if we were not awaiting in the first place
                if (!isPrerequested)
                {
                    Reset();
                }

                _cancellationToken.ThrowIfCancellationRequested();

                return true;
            }

            return false;
        }

        public override string ToString()
        {
            return $"CancelledState: {_cancelledState}, {nameof(IsCompleted)}: {IsCompleted}";
        }

        private enum CancelledState
        {
            NotCancelled = 0,
            CancellationObserved = 1,
            CancellationPreRequested = 2,
            CancellationRequested = 3,
        }
    }
}
