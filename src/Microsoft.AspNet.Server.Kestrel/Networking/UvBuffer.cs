using System;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public struct UvBuffer
    {
        public UvBuffer(IntPtr memory, int len)
        {
            if (Libuv.IsWindows)
            {
                x0 = (IntPtr)len;
                x1 = memory;
            }
            else
            {
                x0 = memory;
                x1 = (IntPtr)len;
            }
        }

        public IntPtr x0; // Win: ULONG, Linux: char*
        public IntPtr x1; // Win: char*, Linux: size_t
    }
}