// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNet.Server.Features;
using Microsoft.AspNet.Server.Kestrel.Filter;
using Microsoft.Extensions.Configuration;

namespace Microsoft.AspNet.Server.Kestrel
{
    public class KestrelServerInformation : IKestrelServerInformation, IServerAddressesFeature
    {
        public ICollection<string> Addresses { get; private set; }

        public int ThreadCount { get; set; }

        public bool NoDelay { get; set; } = true;

        public IConnectionFilter ConnectionFilter { get; set; }

        public void Initialize(IConfiguration configuration)
        {
            var urls = configuration["server.urls"] ?? string.Empty;
            Addresses = new List<string>(urls.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
