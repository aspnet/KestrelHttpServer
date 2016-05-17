// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Pools.Infrastructure
{
    /// <summary>
    /// Memory padded object to prevent any false sharing on a cache line
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct CacheLinePadded<TObject> where TObject : struct
    {
        private fixed byte CachePaddingStart[48];
        public TObject Value;
        private fixed byte CachePaddingEnd[48];
    }
}
