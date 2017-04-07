// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public struct Event : IDisposable
    {
        private IntPtr _handle;

        public bool IsNull => _handle == IntPtr.Zero;

        public static Event Create()
        {
            return RioFunctions.CreateEvent();
        }

        public RioCompletionQueue CreateCompletionQueue(uint queueSize)
        {
            return RioFunctions.CreateCompletionQueue(this, queueSize);
        }

        public void Dispose()
        {
            if (!IsNull)
            {
                RioFunctions.CloseEvent(this);
                _handle = IntPtr.Zero;
            }
        }
    }
}
