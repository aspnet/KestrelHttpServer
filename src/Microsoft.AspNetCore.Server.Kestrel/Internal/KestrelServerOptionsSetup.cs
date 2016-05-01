// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Server.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal
{
    public class KestrelServerOptionsSetup : IConfigureOptions<ServerOptions>
    {
        private IServiceProvider _services;

        public KestrelServerOptionsSetup(IServiceProvider services)
        {
            _services = services;
        }

        public void Configure(ServerOptions options)
        {
            options.ApplicationServices = _services;
        }
    }
}
