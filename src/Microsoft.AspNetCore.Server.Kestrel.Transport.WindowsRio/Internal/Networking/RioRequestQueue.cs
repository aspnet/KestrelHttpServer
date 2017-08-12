// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public struct RioRequestQueue
    {
#pragma warning disable 0169, 0649
        private IntPtr _handle;
#pragma warning restore 0169, 0649

        public bool IsNull => _handle == IntPtr.Zero;

        public void QueueSend(ref RioBufferSegment rioBuffer)
        {
            RioFunctions.QueueSend(this, ref rioBuffer);
        }

        public void Send(ref RioBufferSegment rioBuffer)
        {
            RioFunctions.Send(this, ref rioBuffer);
        }

        public void SendCommit(ref RioBufferSegment rioBuffer)
        {
            RioFunctions.SendCommit(this, ref rioBuffer);
        }

        public void FlushSends()
        {
            RioFunctions.FlushSends(this);
        }

        public void Receive(ref RioBufferSegment rioBuffer)
        {
            RioFunctions.Receive(this, ref rioBuffer);
        }

        public static explicit operator IntPtr(RioRequestQueue queue)
        {
            return queue._handle;
        }

        public static explicit operator RioRequestQueue(IntPtr queue)
        {
            return new RioRequestQueue() { _handle = queue };
        }
    }
}
