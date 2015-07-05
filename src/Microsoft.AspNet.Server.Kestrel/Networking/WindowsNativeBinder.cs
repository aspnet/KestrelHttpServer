using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public sealed class WindowsNativeBinder : IDisposable
    {
        private readonly IntPtr handle;

        public WindowsNativeBinder(string dllFile, Type bindTarget)
        {
            handle = LoadLibrary(dllFile);
            if (handle == IntPtr.Zero)
            {
                throw new DllNotFoundException(dllFile);
            }

            foreach (var field in bindTarget.GetTypeInfo().DeclaredFields)
            {
                var procAddress = GetProcAddress(handle, field.Name);
                if (procAddress == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not load member: " + field.Name);
                }

                var value = Marshal.GetDelegateForFunctionPointer(procAddress, field.FieldType);
                field.SetValue(this, value);
            }
        }

        public void Dispose()
        {
            FreeLibrary(handle);
            GC.SuppressFinalize(this);
        }

        [DllImport("kernel32")]
        private static extern IntPtr LoadLibrary(string dllToLoad);
        [DllImport("kernel32")]
        private static extern bool FreeLibrary(IntPtr hModule);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);
    }
}