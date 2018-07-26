// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.FlowControl
{
    public class Http2InputFlowControl
    {
        private readonly int _initialWindowSize;
        private readonly int _minWindowSizeIncrement;

        private Http2FlowControl _flow;
        private int _unackedBytes;
        private bool _windowUpdatesDisabled;
        private readonly object _flowLock = new object();

        public Http2InputFlowControl(uint initialWindowSize)
        {
            _flow = new Http2FlowControl(initialWindowSize);
            _initialWindowSize = (int)initialWindowSize;
            _minWindowSizeIncrement = _initialWindowSize / 2;
        }

        public bool TryAdvance(int bytes)
        {
            lock (_flowLock)
            {
                // Even if the stream is aborted, the client should never send more data than was available in the
                // flow-control window at the time of the abort.
                if (bytes > _flow.Available)
                {
                    throw new Http2ConnectionErrorException(CoreStrings.Http2ErrorFlowControlWindowExceeded, Http2ErrorCode.FLOW_CONTROL_ERROR);
                }

                if (_flow.IsAborted)
                {
                    // This data won't be read by the app, so tell the caller to count the data as already consumed.
                    return false;
                }

                _flow.Advance(bytes);
                return true;
            }
        }

        public bool TryUpdateWindow(int bytes, out int updateSize)
        {
            lock (_flowLock)
            {
                updateSize = 0;

                if (_flow.IsAborted)
                {
                    // All data received by stream has already been returned to the connection window.
                    return false;
                }

                if (!_flow.TryUpdateWindow(bytes))
                {
                    // We only try to update the window back to its initial size after the app consumes data.
                    // It shouldn't be possible for the window size to ever exceed Http2PeerSettings.MaxWindowSize.
                    Debug.Assert(false, $"{nameof(TryUpdateWindow)} attempted to grow window past max size.");
                }

                if (_windowUpdatesDisabled)
                {
                    // Continue returning space to the connection window. The end of the stream has already
                    // been received, so don't send window updates for the stream window.
                    return true;
                }

                var potentialUpdateSize = _unackedBytes + bytes;

                if (potentialUpdateSize > _minWindowSizeIncrement)
                {
                    _unackedBytes = 0;
                    updateSize = potentialUpdateSize;
                }
                else
                {
                    _unackedBytes = potentialUpdateSize;
                }

                return true;
            }
        }

        public void StopWindowUpdates()
        {
            lock (_flowLock)
            {
                _windowUpdatesDisabled = true;
            }
        }

        public int Abort()
        {
            lock (_flowLock)
            {
                _flow.Abort();

                // Tell caller to return connection window space consumed by this stream.
                // Even if window updates have been disable at the stream level, connection-level window updates may
                // still be necessary.
                return _initialWindowSize - _flow.Available;
            }
        }
    }
}
