// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    [Flags]
    internal enum ResultFlags : byte
    {
        None = 0,
        Cancelled = 1,
        Completed = 2
    }
}