// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
#define TRACE
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    /// <summary>
    /// Summary description for UvMemory
    /// </summary>
    public abstract class UvMemory : SafeHandle
    {
        private readonly int _threadId;

        public UvMemory(int threadId, int size)
            : base(IntPtr.Zero, true)
        {
            _threadId = threadId;

            handle = Marshal.AllocCoTaskMem(size);
            var weakHandle = GCHandle.ToIntPtr(GCHandle.Alloc(this, GCHandleType.Weak));
            Marshal.WriteIntPtr(handle, weakHandle);
        }

        public override bool IsInvalid
        {
            get
            {
                return handle == IntPtr.Zero;
            }
        }

        public int ThreadId
        {
            get
            {
                return _threadId;
            }
        }

        protected static void DestroyMemory(IntPtr memory)
        {
            var gcHandlePtr = Marshal.ReadIntPtr(memory);
            if (gcHandlePtr != IntPtr.Zero)
            {
                var gcHandle = GCHandle.FromIntPtr(gcHandlePtr);
                gcHandle.Free();
            }
            Marshal.FreeCoTaskMem(memory);
        }

        public void Validate(bool closed = false)
        {
            Trace.Assert(closed || !IsClosed, "Handle is closed");
            Trace.Assert(!IsInvalid, "Handle is invalid");
            Trace.Assert(_threadId == Thread.CurrentThread.ManagedThreadId, "ThreadId is incorrect");
        }

        public static THandle FromIntPtr<THandle>(IntPtr handle)
        {
            var weakHandle = Marshal.ReadIntPtr(handle);
            var gcHandle = GCHandle.FromIntPtr(weakHandle);
            return (THandle)gcHandle.Target;
        }
    }
}