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
        public ICollection<string> Addresses { get; } = new List<string>();

        public int ThreadCount { get; set; }

        public bool NoDelay { get; set;  }

        public int MaxHeaderBytes { get; set; } = 16384; // 16kB

        public long MaxUploadBytes { get; set; } = 8388608; // 8MB

        public IConnectionFilter ConnectionFilter { get; set; }

        public void Initialize(IConfiguration configuration)
        {
            var urls = configuration["server.urls"] ?? string.Empty;
            foreach (var url in urls.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Addresses.Add(url);
            }

            var maxHeaderBytes = configuration["request.maxHeaderBytes"];
            if (!string.IsNullOrEmpty(maxHeaderBytes))
            {
                int value;
                if (!int.TryParse(maxHeaderBytes, out value))
                {
                    throw new ArgumentException("maxHeaderBytes must be an integer 1024 or greater", "request.maxHeaderBytes");
                }
                if (value < 1024)
                {
                    throw new ArgumentOutOfRangeException("request.maxHeaderBytes", maxHeaderBytes, "maxHeaderBytes must be 1024 or greater");
                }
                MaxHeaderBytes = value;
            }

            var maxUploadBytes = configuration["request.maxUploadBytes"];
            if (!string.IsNullOrEmpty(maxHeaderBytes))
            {
                long value;
                if (!long.TryParse(maxHeaderBytes, out value))
                {
                    throw new ArgumentException("maxUploadBytes must be an integer 0 or greater", "request.maxUploadBytes");
                }
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("request.maxUploadBytes", maxUploadBytes, "maxUploadBytes must be a positive integer");
                }
                MaxUploadBytes = value;
            }
        }
    }
}
