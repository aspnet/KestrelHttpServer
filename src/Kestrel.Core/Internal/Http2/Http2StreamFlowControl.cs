// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2StreamFlowControl
    {
        private readonly Http2FlowControl _connectionLevelFlowControl;
        private readonly Http2FlowControl _streamLevelFlowControl;

        public Http2StreamFlowControl(Http2FlowControl connectionLevelFlowControl, uint initialWindowSize)
        {
            _connectionLevelFlowControl = connectionLevelFlowControl;
            _streamLevelFlowControl = new Http2FlowControl(initialWindowSize);
        }

        public int Available => Math.Min(_connectionLevelFlowControl.Available, _streamLevelFlowControl.Available);

        public int Take(long bytes, out Http2FlowControlAwaitable awaitable)
        {
            var leastAvailableFlow = _streamLevelFlowControl.Available < _connectionLevelFlowControl.Available
                ? _streamLevelFlowControl : _connectionLevelFlowControl;

            var actual = (int)Math.Min(bytes, leastAvailableFlow.Available);

            // Make sure to advance prior to accessing AvailabilityAwaitable.
            _connectionLevelFlowControl.Advance(actual);
            _streamLevelFlowControl.Advance(actual);
            awaitable = actual == bytes ? null : leastAvailableFlow.AvailabilityAwaitable;

            return actual;
        }

        // The connection-level update window is updated independently.
        // https://httpwg.org/specs/rfc7540.html#rfc.section.6.9.1
        public bool TryUpdateWindow(int bytes)
        {
            return _streamLevelFlowControl.TryUpdateWindow(bytes);
        }
    }
}
