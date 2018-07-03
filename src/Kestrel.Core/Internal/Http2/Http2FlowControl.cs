﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2FlowControl
    {
        // MaxWindowSize must be a long to prevent overflows in TryUpdateWindow.
        public const long MaxWindowSize = int.MaxValue;

        private readonly Queue<Http2FlowControlAwaitable> _awaitableQueue = new Queue<Http2FlowControlAwaitable>();
        private readonly Queue<Http2FlowControlAwaitable> _awaitablePool = new Queue<Http2FlowControlAwaitable>();

        public Http2FlowControl(uint initialWindowSize)
        {
            Debug.Assert(initialWindowSize <= MaxWindowSize, $"{nameof(initialWindowSize)} too large.");

            Available = (int)initialWindowSize;
        }

        public int Available { get; private set; }
        public bool IsAborted { get; private set; }

        public Http2FlowControlAwaitable AvailabilityAwaitable
        {
            get
            {
                Debug.Assert(!IsAborted, $"({nameof(AvailabilityAwaitable)} accessed after abort.");
                Debug.Assert(Available <= 0, $"({nameof(AvailabilityAwaitable)} accessed with {Available} bytes available.");

                var awaitable = _awaitablePool.Count > 0 ? _awaitablePool.Dequeue() : new Http2FlowControlAwaitable();
                _awaitableQueue.Enqueue(awaitable);
                return awaitable;
            }
        }

        public void Advance(int bytes)
        {
            Debug.Assert(!IsAborted, $"({nameof(Advance)} called after abort.");
            Debug.Assert(bytes >= 0 && bytes <= Available, $"{nameof(Advance)}({bytes}) called with {Available} bytes available.");

            Available -= bytes;
        }

        // bytes can be negative when SETTINGS_INITIAL_WINDOW_SIZE decreases mid-connection.
        // This can also cause Available to become negative which MUST be allowed.
        // https://httpwg.org/specs/rfc7540.html#rfc.section.6.9.2
        public bool TryUpdateWindow(int bytes)
        {
            var maxUpdate = MaxWindowSize - Available;

            if (bytes > maxUpdate)
            {
                return false;
            }

            Available += bytes;

            while (Available > 0 && _awaitableQueue.Count > 0)
            {
                var awaitable = _awaitableQueue.Dequeue();
                awaitable.Complete();
            }

            return true;
        }

        public void Abort()
        {
            IsAborted = true;

            while (_awaitableQueue.Count > 0)
            {
                _awaitableQueue.Dequeue().Complete();
            }
        }

        public void ReturnAwaitable(Http2FlowControlAwaitable awaitable)
        {
            _awaitablePool.Enqueue(awaitable);
        }
    }
}
