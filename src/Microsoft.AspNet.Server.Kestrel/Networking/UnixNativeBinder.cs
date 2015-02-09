using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public sealed class UnixNativeBinder : IDisposable
    {
        private readonly IntPtr handle;

        public UnixNativeBinder(string soFile, Type bindTarget)
        {
            handle = dlopen(soFile, 2);
            if (handle == IntPtr.Zero)
            {
                throw new DllNotFoundException(soFile);
            }

            foreach (var field in bindTarget.GetTypeInfo().DeclaredFields)
            {
                dlerror();
                var pointer = dlsym(handle, field.Name);
                var error = dlerror();
                if (error != IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not load member: " + field.Name);
                }

                var value = Marshal.GetDelegateForFunctionPointer(pointer, field.FieldType);
                field.SetValue(this, value);
            }
        }

        public void Dispose()
        {
            dlclose(handle);
            GC.SuppressFinalize(this);
        }

        [DllImport("dl")]
        private static extern IntPtr dlopen(string fileName, int flags);
        [DllImport("dl")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);
        [DllImport("dl")]
        private static extern int dlclose(IntPtr handle);
        [DllImport("dl")]
        private static extern IntPtr dlerror();
    }
}
