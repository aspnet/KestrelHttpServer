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
        public Task AvailabilityTask => Task.WhenAll(_connectionLevelFlowControl.AvailabilityTask, _streamLevelFlowControl.AvailabilityTask);

        public void Advance(int bytes)
        {
            _connectionLevelFlowControl.Advance(bytes);
            _streamLevelFlowControl.Advance(bytes);
        }

        // The connection-level update window is updated independently.
        // https://httpwg.org/specs/rfc7540.html#rfc.section.6.9.1
        public bool TryUpdateWindow(int bytes)
        {
            return _streamLevelFlowControl.TryUpdateWindow(bytes);
        }
    }
}
