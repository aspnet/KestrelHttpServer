// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2StreamFlowControl
    {
        private readonly Http2FlowControl _connectionLevelFlowControl;
        private readonly Http2FlowControl _streamLevelFlowControl;

        private Http2FlowControl _lastAwaitedFlowControl;
        private Http2FlowControlAwaitable _lastAwaitable;

        public Http2StreamFlowControl(Http2FlowControl connectionLevelFlowControl, uint initialWindowSize)
        {
            _connectionLevelFlowControl = connectionLevelFlowControl;
            _streamLevelFlowControl = new Http2FlowControl(initialWindowSize);
        }

        public int Available => Math.Min(_connectionLevelFlowControl.Available, _streamLevelFlowControl.Available);

        public bool IsAborted => _connectionLevelFlowControl.IsAborted || _streamLevelFlowControl.IsAborted;

        public int Advance(long bytes, out Http2FlowControlAwaitable awaitable)
        {
            Debug.Assert(!IsAborted, $"({nameof(Advance)} called after abort.");

            // IMPORTANT: Don't pool aborted awaitables because their Complete() method will
            // likely be called multiple times.
            _lastAwaitedFlowControl?.ReturnAwaitable(_lastAwaitable);

            var leastAvailableFlow = _connectionLevelFlowControl.Available < _streamLevelFlowControl.Available
                ? _connectionLevelFlowControl : _streamLevelFlowControl;

            var actual = (int)Math.Min(bytes, leastAvailableFlow.Available);

            // Make sure to advance prior to accessing AvailabilityAwaitable.
            _connectionLevelFlowControl.Advance(actual);
            _streamLevelFlowControl.Advance(actual);

            if (actual < bytes)
            {
                _lastAwaitedFlowControl = leastAvailableFlow;
                _lastAwaitable = leastAvailableFlow.AvailabilityAwaitable;
            }
            else
            {
                _lastAwaitedFlowControl = null;
                _lastAwaitable = null;
            }

            awaitable = _lastAwaitable;

            return actual;
        }

        // The connection-level update window is updated independently.
        // https://httpwg.org/specs/rfc7540.html#rfc.section.6.9.1
        public bool TryUpdateWindow(int bytes)
        {
            return _streamLevelFlowControl.TryUpdateWindow(bytes);
        }

        public void Abort()
        {
            _streamLevelFlowControl.Abort();

            // If this stream is waiting on a connection-level window update, complete this stream's
            // connection-level awaitable so the stream abort is observed immediately.
            // This could complete an awaitable still sitting in the connection-level awaitable queue,
            // but this is safe because the awaitable will not be repooled after an abort is observed.
            if (_lastAwaitedFlowControl == _connectionLevelFlowControl)
            {
                _lastAwaitable.Complete();
                _lastAwaitedFlowControl = null;
                _lastAwaitable = null;
            }
        }
    }
}
