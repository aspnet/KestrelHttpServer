using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Kestrel.LibraryLoader
{
    public abstract class Library
    {
        public Library()
        {
            IsWindows = PlatformApis.IsWindows();
            if (!IsWindows)
            {
                IsDarwin = PlatformApis.IsDarwin();
            }
        }

        public bool IsWindows;
        public bool IsDarwin;

        public Func<string, IntPtr> LoadLibrary;
        public Func<IntPtr, bool> FreeLibrary;
        public Func<IntPtr, string, IntPtr> GetProcAddress;

        public virtual void Load(string dllToLoad)
        {
            PlatformApis.Apply(this);

            var module = LoadLibrary(dllToLoad);
            if (module == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to load library.");
            }

            foreach (var field in GetType().GetTypeInfo().DeclaredFields)
            {
                var procAddress = GetProcAddress(module, field.Name.TrimStart('_'));
                if (procAddress == IntPtr.Zero)
                {
                    continue;
                }
                var value = Marshal.GetDelegateForFunctionPointer(procAddress, field.FieldType);
                field.SetValue(this, value);
            }
        }
    }
}
