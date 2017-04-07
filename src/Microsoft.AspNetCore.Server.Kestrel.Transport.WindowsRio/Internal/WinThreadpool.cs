// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public class WinThreadpool
    {
        public static WinThreadpoolWait CreateThreadpoolWait(WaitCallback pfnwk, IntPtr state)
        {
            return NativeMethods.CreateThreadpoolWait(pfnwk, state, IntPtr.Zero);
        }
        public static WinThreadpoolWork CreateThreadpoolWork(WorkCallback pfnwk, IntPtr state)
        {
            return NativeMethods.CreateThreadpoolWork(pfnwk, state, IntPtr.Zero);
        }

        public static void SetThreadpoolWait(WinThreadpoolWait wait, Event @event)
        {
            NativeMethods.SetThreadpoolWait(wait, @event, IntPtr.Zero);
        }
        public static void SubmitThreadpoolWork(WinThreadpoolWork work)
        {
            NativeMethods.SubmitThreadpoolWork(work);
        }

        public static void CloseThreadpoolWait(WinThreadpoolWait wait)
        {
            NativeMethods.CloseThreadpoolWait(wait);
        }

        private static class NativeMethods
        {
            const string Kernel_32 = "Kernel32";

            [SuppressUnmanagedCodeSecurity]
            [DllImport(Kernel_32, SetLastError = true)]
            public static extern WinThreadpoolWork CreateThreadpoolWork(WorkCallback pfnwk, IntPtr state, IntPtr pcbe);

            [SuppressUnmanagedCodeSecurity]
            [DllImport(Kernel_32, SetLastError = true)]
            public static extern WinThreadpoolWait CreateThreadpoolWait(WaitCallback pfnwk, IntPtr state, IntPtr pcbe);

            [SuppressUnmanagedCodeSecurity]
            [DllImport(Kernel_32, SetLastError = true)]
            public static extern void SetThreadpoolWait(WinThreadpoolWait pwa, Event h, IntPtr pftTimeout);

            [SuppressUnmanagedCodeSecurity]
            [DllImport(Kernel_32, SetLastError = true)]
            public static extern void SubmitThreadpoolWork(WinThreadpoolWork pwa);

            [SuppressUnmanagedCodeSecurity]
            [DllImport(Kernel_32, SetLastError = true)]
            public static extern void CloseThreadpoolWait(WinThreadpoolWait pwa);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WinThreadpoolWork
    {
        private IntPtr _handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WinThreadpoolWait : IDisposable
    {
        private IntPtr _handle;

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                WinThreadpool.CloseThreadpoolWait(this);
                _handle = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WinCallbackInstance
    {
        private IntPtr _handle;
    }

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate void WorkCallback([In] WinCallbackInstance Instance, [In] IntPtr Context, [In] WinThreadpoolWork Work);


    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate void WaitCallback([In] WinCallbackInstance Instance, [In] IntPtr Context, [In] WinThreadpoolWait Wait, [In] IntPtr WaitResult);
}
