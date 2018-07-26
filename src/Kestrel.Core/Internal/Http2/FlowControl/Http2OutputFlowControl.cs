// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.FlowControl
{
    public class Http2OutputFlowControl
    {
        private Http2FlowControl _flow;
        private Queue<Http2OutputFlowControlAwaitable> _awaitableQueue;

        public Http2OutputFlowControl(uint initialWindowSize)
        {
            _flow = new Http2FlowControl(initialWindowSize);
        }

        public int Available => _flow.Available;
        public bool IsAborted => _flow.IsAborted;

        public Http2OutputFlowControlAwaitable AvailabilityAwaitable
        {
            get
            {
                Debug.Assert(!_flow.IsAborted, $"({nameof(AvailabilityAwaitable)} accessed after abort.");
                Debug.Assert(_flow.Available <= 0, $"({nameof(AvailabilityAwaitable)} accessed with {Available} bytes available.");

                if (_awaitableQueue == null)
                {
                    _awaitableQueue = new Queue<Http2OutputFlowControlAwaitable>();
                }

                var awaitable = new Http2OutputFlowControlAwaitable();
                _awaitableQueue.Enqueue(awaitable);
                return awaitable;
            }
        }

        public void Advance(int bytes)
        {
            _flow.Advance(bytes);
        }

        // bytes can be negative when SETTINGS_INITIAL_WINDOW_SIZE decreases mid-connection.
        // This can also cause Available to become negative which MUST be allowed.
        // https://httpwg.org/specs/rfc7540.html#rfc.section.6.9.2
        public bool TryUpdateWindow(int bytes)
        {
            if (_flow.TryUpdateWindow(bytes))
            {
                while (_flow.Available > 0 && _awaitableQueue?.Count > 0)
                {
                    _awaitableQueue.Dequeue().Complete();
                }

                return true;
            }

            return false;
        }

        public void Abort()
        {
            // Make sure to set the aborted flag before running any continuations.
            _flow.Abort();

            while (_awaitableQueue?.Count > 0)
            {
                _awaitableQueue.Dequeue().Complete();
            }
        }
    }
}
