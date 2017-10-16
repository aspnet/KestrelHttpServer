// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    internal struct PipeCompletion
    {
        private static readonly ArrayPool<PipeCompletionCallback> CompletionCallbackPool = ArrayPool<PipeCompletionCallback>.Shared;

        private const int InitialCallbacksSize = 1;
        private static readonly Exception _completedNoException = new Exception();

#if COMPLETION_LOCATION_TRACKING
        private string _completionLocation;
#endif
        private Exception _exception;

        private PipeCompletionCallback[] _callbacks;
        private int _callbackCount;

        public string Location
        {
            get
            {
#if COMPLETION_LOCATION_TRACKING
                return _completionLocation;
#else
                return null;
#endif
            }
        }

        public bool IsCompleted => _exception != null;

        public PipeCompletionCallbacks TryComplete(Exception exception = null)
        {
#if COMPLETION_LOCATION_TRACKING
            _completionLocation = Environment.StackTrace;
#endif
            if (_exception == null)
            {
                // Set the exception object to the exception passed in or a sentinel value
                _exception = exception ?? _completedNoException;
            }
            return GetCallbacks();
        }

        public PipeCompletionCallbacks AddCallback(Action<Exception, object> callback, object state)
        {
            if (_callbacks == null)
            {
                _callbacks = CompletionCallbackPool.Rent(InitialCallbacksSize);
            }

            var newIndex = _callbackCount;
            _callbackCount++;

            if (newIndex == _callbacks.Length)
            {
                var newArray = new PipeCompletionCallback[_callbacks.Length * 2];
                Array.Copy(_callbacks, newArray, _callbacks.Length);
                CompletionCallbackPool.Return(_callbacks);
                _callbacks = newArray;
            }
            _callbacks[newIndex].Callback = callback;
            _callbacks[newIndex].State = state;

            if (IsCompleted)
            {
                return GetCallbacks();
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsCompletedOrThrow()
        {
            if (_exception == null)
            {
                return false;
            }

            if (_exception != _completedNoException)
            {
                ThrowFailed();
            }

            return true;
        }

        private PipeCompletionCallbacks GetCallbacks()
        {
            Debug.Assert(IsCompleted);
            if (_callbackCount == 0)
            {
                return null;
            }

            var callbacks = new PipeCompletionCallbacks(CompletionCallbackPool,
                _callbackCount,
                _exception == _completedNoException ? null : _exception,
                _callbacks);

            _callbacks = null;
            _callbackCount = 0;
            return callbacks;
        }

        public void Reset()
        {
            Debug.Assert(IsCompleted);
            Debug.Assert(_callbacks == null);
            _exception = null;
#if COMPLETION_LOCATION_TRACKING
            _completionLocation = null;
#endif
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowFailed()
        {
            throw _exception;
        }

        public override string ToString()
        {
            return $"{nameof(IsCompleted)}: {IsCompleted}";
        }
    }
}
