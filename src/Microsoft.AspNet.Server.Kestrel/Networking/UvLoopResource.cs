// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public abstract class UvLoopResource : IDisposable
    {
        private static readonly uv_close_cb _destroyMemory = DestroyMemory;

        private readonly int _threadId;

        protected UvLoopResource(int threadId, int size)
        {
            _threadId = threadId;

            Handle = Marshal.AllocCoTaskMem(size);
        }

        public IntPtr Handle { get; private set; }

        public void Validate()
        {
            Trace.Assert(_threadId == Thread.CurrentThread.ManagedThreadId, "ThreadId is incorrect");
            if (Handle == IntPtr.Zero)
                throw new ObjectDisposedException(GetType().Name);
        }

        private static void DestroyMemory(IntPtr memory)
        {
            Marshal.FreeCoTaskMem(memory);
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(UvLoopResource));
            Validate();
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~UvLoopResource()
        {
            // If the finalizer is called, this means Dispose was not.
            // In that case, there is no way to know if the event loop is still active
            //  and so uv_close cannot be reliably expected to call back.
            // However, the code still frees the unmanaged resources,
            //  so expect something to blow up later.

            // There is one common case of this happening,
            //  namely when handles need to be manually closed during shutdown

            Console.WriteLine("TODO: Warning! UvHandle was finalized instead of disposed. This is either a bug in Kestrel (unlikely) or some other code didn't close all resources before Kestrel was stopped.");
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
