// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Pools.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Pools
{
    public class StreamFactory : ComponentFactory<Streams>
    {
        public StreamFactory() : base()
        {
        }

        public StreamFactory(int MaxPooled) : base (MaxPooled)
        {
        }

        // https://github.com/dotnet/coreclr/pull/4468#issuecomment-212931043
        // 12x Faster than new T() which uses System.Activator 
        protected override Streams CreateNew() => new Streams();
    }
}
