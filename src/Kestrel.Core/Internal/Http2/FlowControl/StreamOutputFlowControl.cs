﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.FlowControl
{
    public class StreamOutputFlowControl
    {
        private readonly OutputFlowControl _connectionLevelFlowControl;
        private readonly OutputFlowControl _streamLevelFlowControl;

        private OutputFlowControlAwaitable _currentConnectionLevelAwaitable;

        public StreamOutputFlowControl(OutputFlowControl connectionLevelFlowControl, uint initialWindowSize)
        {
            _connectionLevelFlowControl = connectionLevelFlowControl;
            _streamLevelFlowControl = new OutputFlowControl(initialWindowSize);
        }

        public int Available => Math.Min(_connectionLevelFlowControl.Available, _streamLevelFlowControl.Available);

        public bool IsAborted => _connectionLevelFlowControl.IsAborted || _streamLevelFlowControl.IsAborted;

        public void Advance(int bytes)
        {
            _connectionLevelFlowControl.Advance(bytes);
            _streamLevelFlowControl.Advance(bytes);
        }

        public int AdvanceUpToAndWait(long bytes, out OutputFlowControlAwaitable awaitable)
        {
            var leastAvailableFlow = _connectionLevelFlowControl.Available < _streamLevelFlowControl.Available
                ? _connectionLevelFlowControl : _streamLevelFlowControl;
 
            // Clamp ~= Math.Clamp from netcoreapp >= 2.0
            var actual = Clamp(leastAvailableFlow.Available, 0, bytes);

            // Make sure to advance prior to accessing AvailabilityAwaitable.
            _connectionLevelFlowControl.Advance(actual);
            _streamLevelFlowControl.Advance(actual);

            awaitable = null;
            _currentConnectionLevelAwaitable = null;

            if (actual < bytes)
            {
                awaitable = leastAvailableFlow.AvailabilityAwaitable;

                if (leastAvailableFlow == _connectionLevelFlowControl)
                {
                    _currentConnectionLevelAwaitable = awaitable;
                }
            }

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
            // but this is safe because completing it again will just no-op.
            _currentConnectionLevelAwaitable?.Complete();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Clamp(int value, int min, long max)
        {
            Debug.Assert(min <= max, $"{nameof(Clamp)} called with a min greater than the max.");

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return (int)max;
            }

            return value;
        }
    }
}
