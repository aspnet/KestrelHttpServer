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
        private int _threadId;

        public UvMemory(int threadId, int size)
            : base(IntPtr.Zero, true)
        {
            ThreadId = threadId;

            handle = Marshal.AllocCoTaskMem(size);
            unsafe
            {
                *(IntPtr*)handle = GCHandle.ToIntPtr(GCHandle.Alloc(this, GCHandleType.Weak));
            }
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
            private set
            {
                _threadId = value;
            }
        }

        unsafe protected static void DestroyMemory(IntPtr memory)
        {
            var gcHandlePtr = *(IntPtr*)memory;
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

        unsafe public static THandle FromIntPtr<THandle>(IntPtr handle)
        {
            GCHandle gcHandle = GCHandle.FromIntPtr(*(IntPtr*)handle);
            return (THandle)gcHandle.Target;
        }

    }
}