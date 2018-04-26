// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public static class TransportSelector
    {
        public static IWebHostBuilder GetWebHostBuilder(bool supressMemoryPoolDisposeException = false)
        {
            return new WebHostBuilder().UseLibuv(options => {
                options.MemoryPoolFactory = () => KestrelMemoryPool.Create(supressMemoryPoolDisposeException);
            }).ConfigureServices(TestServer.RemoveDevCert);
        }
    }
}
