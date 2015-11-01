﻿using Microsoft.AspNet.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNet.Server.KestrelTests
{
    public static class MemoryPoolExtensions
    {
        public static MemoryPoolIterator Add(this MemoryPoolIterator iterator, int count)
        {
            int actual;
            return iterator.CopyTo(new byte[count], 0, count,  out actual);
        } 
    }
}
