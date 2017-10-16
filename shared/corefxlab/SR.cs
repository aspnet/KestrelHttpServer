namespace System
{
    // This is generated as part of corefx build process
    internal static class SR
    {
        internal static string ArrayTypeMustBeExactMatch
        {
            get
            {
                return System.SR.GetResourceString("ArrayTypeMustBeExactMatch", null);
            }
        }

        internal static string CannotCallEqualsOnSpan
        {
            get
            {
                return System.SR.GetResourceString("CannotCallEqualsOnSpan", null);
            }
        }

        internal static string CannotCallGetHashCodeOnSpan
        {
            get
            {
                return System.SR.GetResourceString("CannotCallGetHashCodeOnSpan", null);
            }
        }

        internal static string Argument_InvalidTypeWithPointersNotSupported
        {
            get
            {
                return System.SR.GetResourceString("Argument_InvalidTypeWithPointersNotSupported", null);
            }
        }

        internal static string Argument_DestinationTooShort
        {
            get
            {
                return System.SR.GetResourceString("Argument_DestinationTooShort", null);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool UsingResourceKeys()
        {
            return false;
        }

        internal static string GetResourceString(string resourceKey, string defaultString)
        {
            return resourceKey;
        }

        internal static string Format(string resourceFormat, params object[] args)
        {
             return resourceFormat + string.Join(", ", args);
        }

        internal static string Format(string resourceFormat, object p1)
        {
            if (System.SR.UsingResourceKeys())
            {
                return string.Join(", ", new object[]
                {
                    resourceFormat,
                    p1
                });
            }
            return string.Format(resourceFormat, p1);
        }

        internal static string Format(string resourceFormat, object p1, object p2)
        {
            if (System.SR.UsingResourceKeys())
            {
                return string.Join(", ", new object[]
                {
                    resourceFormat,
                    p1,
                    p2
                });
            }
            return string.Format(resourceFormat, p1, p2);
        }

        internal static string Format(string resourceFormat, object p1, object p2, object p3)
        {
            if (System.SR.UsingResourceKeys())
            {
                return string.Join(", ", new object[]
                {
                    resourceFormat,
                    p1,
                    p2,
                    p3
                });
            }
            return string.Format(resourceFormat, p1, p2, p3);
        }
    }
}

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers.Internal
{
    // We don't want to pull System.Buffers required by ManagedBufferPool implemenation
    // and BufferPool depends on ManagedBufferPool.Shared
    internal class ManagedBufferPool
    {
        public static BufferPool Shared { get; } = null;
    }
}