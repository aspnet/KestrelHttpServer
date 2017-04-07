// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    /// <summary>
    /// Simple awaitable gate - intended to synchronize a single producer with a single consumer to ensure the producer doesn't
    /// produce until the consumer is ready. Similar to a <see cref="TaskCompletionSource{TResult}"/> but reusable so we don't have
    /// to keep allocating new ones every time.
    /// </summary>
    /// <remarks>
    /// The gate can be in one of two states: "Open", indicating that an await will immediately return and "Closed", meaning that an await
    /// will block until the gate is opened. The gate is initially "Closed" and can be opened by a call to <see cref="Open"/>. Upon the completion
    /// of an await, it will automatically return to the "Closed" state (this is done in the <see cref="GetResult"/> call that is injected by the
    /// compiler's async/await logic).
    /// </remarks>
    public class AutoResetGate<T> : ICriticalNotifyCompletion
    {
        private static readonly Action _gateIsOpen = () => { };

        private Action _gateState;
        private T _value;

        /// <summary>
        /// Returns a boolean indicating if the gate is "open"
        /// </summary>
        public bool IsCompleted => ReferenceEquals(_gateState, _gateIsOpen);

        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

        public void OnCompleted(Action continuation)
        {
            // If we're already completed, call the continuation immediately
            if (IsCompleted)
            {
                continuation();
            }
            else
            {
                // Otherwise, if the current continuation is null, atomically store the new continuation in the field and return the old value
                var previous = Interlocked.CompareExchange(ref _gateState, continuation, null);
                if (ReferenceEquals(previous, _gateIsOpen))
                {
                    // It got completed in the time between the previous the method and the cmpexch.
                    // So call the continuation (the value of _continuation will remain _completed because cmpexch is atomic,
                    // so we didn't accidentally replace it).
                    continuation();
                }
            }
        }

        /// <summary>
        /// Resets the gate to continue blocking the waiter. This is called immediately after awaiting the signal.
        /// </summary>
        public T GetResult()
        {
            var value = _value;
            _value = default(T);

            // Clear the active continuation to "reset" the state of this event
            _gateState = null;

            return value;
        }

        /// <summary>
        /// Set the gate to allow the waiter to continue.
        /// </summary>
        public void Open(T value)
        {
            // Set the stored continuation value to a sentinel that indicates the state is completed, then call the previous value.
            _value = value;
            var completion = Interlocked.Exchange(ref _gateState, _gateIsOpen);
            if (!ReferenceEquals(completion, _gateIsOpen))
            {
                completion?.Invoke();
            }
        }

        private void Close()
        {
            // Clear the active continuation to "reset" the state of this event
            _gateState = null;
        }

        public AutoResetGate<T> GetAwaiter() => this;

        private void ThrowMultiConsumerUseError()
        {
            throw new InvalidOperationException();
        }
    }
}
