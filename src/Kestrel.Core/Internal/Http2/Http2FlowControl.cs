// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2FlowControl
    {
        private bool _awaitingAvailability;
        private TaskCompletionSource<object> _availabilityTcs;

        public int Available { get; private set; }

        public Http2FlowControl(uint initialWindowSize)
        {
            Debug.Assert(initialWindowSize <= int.MaxValue, $"{nameof(initialWindowSize)} too large.");

            Available = (int)initialWindowSize;
        }

        // TODO: Cancel this task during connection and stream aborts.
        public Task AvailabilityTask
        {
            get
            {
                if (Available > 0)
                {
                    return Task.CompletedTask;
                }

                if (!_awaitingAvailability)
                {
                    _awaitingAvailability = true;
                    _availabilityTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                return _availabilityTcs.Task;
            }
        }

        public void Advance(int bytes)
        {
            Debug.Assert(bytes <= Available, $"{nameof(Advance)}({bytes}) called with only {Available} bytes available.");

            Available -= bytes;
        }

        // bytes can be negative when SETTINGS_INITIAL_WINDOW_SIZE decreases mid-connection.
        // This can also cause Available to become negative which MUST be allowed.
        // https://httpwg.org/specs/rfc7540.html#rfc.section.6.9.2
        public bool TryUpdateWindow(int bytes)
        {
            var maxUpdate = (long)int.MaxValue - Available;

            if (bytes > maxUpdate)
            {
                return false;
            }

            Available += bytes;

            if (Available > 0 && _awaitingAvailability)
            {
                // Set _awaitingAvailability before setting Tcs because the AvailabilityTask
                // can be awaited inline.
                _awaitingAvailability = false;
                _availabilityTcs.SetResult(null);
            }

            return true;
        }
    }
}
