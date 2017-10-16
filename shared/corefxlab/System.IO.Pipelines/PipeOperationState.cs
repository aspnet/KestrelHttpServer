// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    internal struct PipeOperationState
    {
        private State _state;
#if OPERATION_LOCATION_TRACKING
        private string _operationStartLocation;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Begin(ExceptionResource exception)
        {
            // Inactive and Tenative are allowed
            if (_state == State.Active)
            {
                PipelinesThrowHelper.ThrowInvalidOperationException(exception, Location);
            }

            _state = State.Active;

#if OPERATION_LOCATION_TRACKING
            _operationStartLocation = Environment.StackTrace;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginTentative(ExceptionResource exception)
        {
            // Inactive and Tenative are allowed
            if (_state == State.Active)
            {
                PipelinesThrowHelper.ThrowInvalidOperationException(exception, Location);
            }

            _state = State.Tentative;

#if OPERATION_LOCATION_TRACKING
            _operationStartLocation = Environment.StackTrace;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void End(ExceptionResource exception)
        {
            if (_state == State.Inactive)
            {
                PipelinesThrowHelper.ThrowInvalidOperationException(exception, Location);
            }

            _state = State.Inactive;
#if OPERATION_LOCATION_TRACKING
            _operationStartLocation = null;
#endif
        }

        public bool IsActive => _state == State.Active;

        public string Location
        {
            get
            {
#if OPERATION_LOCATION_TRACKING
                return _operationStartLocation;
#else
                return null;
#endif
            }
        }

        public override string ToString()
        {
            return $"State: {_state}";
        }
    }

    internal enum State: byte
    {
        Inactive = 1,
        Active = 2,
        Tentative = 3
    }
}
