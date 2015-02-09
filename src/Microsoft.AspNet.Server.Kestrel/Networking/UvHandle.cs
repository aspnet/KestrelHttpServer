// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public abstract class UvHandle : IDisposable
    {
        private static readonly uv_close_cb _destroyMemory = DestroyMemory;

        private readonly int _threadId;

        protected UvHandle(int threadId,int size)
        {
            _threadId = threadId;

            Handle = Marshal.AllocCoTaskMem(size);
            var weakHandle = GCHandle.ToIntPtr(GCHandle.Alloc(this, GCHandleType.Weak));
            Marshal.WriteIntPtr(Handle, weakHandle);
        }

        public IntPtr Handle { get; private set; }

        public void Validate()
        {
            Trace.Assert(_threadId == Thread.CurrentThread.ManagedThreadId, "ThreadId is incorrect");
            Trace.Assert(Handle != IntPtr.Zero, "Handle in invalid");
        }

        private static void DestroyMemory(IntPtr memory)
        {
            var gcHandlePtr = Marshal.ReadIntPtr(memory);
            if (gcHandlePtr != IntPtr.Zero)
            {
                var gcHandle = GCHandle.FromIntPtr(gcHandlePtr);
                gcHandle.Free();
            }
            Marshal.FreeCoTaskMem(memory);
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(UvHandle));
            Validate();
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~UvHandle()
        {
            // If the finalizer is called, this means Dispose was not.
            // In that case, there is no way to know if the event loop is still active
            //  and so uv_close cannot be reliably expected to call back.
            // However, the code still frees the unmanaged resources,
            //  so expect something to blow up later.

            Console.WriteLine("TODO: Warning! UvHandle was finalized instead of disposed. This is a bug in Kestrel.");
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // After this call, the managed object is ready for collection.
                // The unmanaged memory is being passed to the callback to free
                UnsafeNativeMethods.uv_close(Handle, _destroyMemory);
            }
            else
            {
                DestroyMemory(Handle);
            }

            Handle = IntPtr.Zero;
        }
    }
}
