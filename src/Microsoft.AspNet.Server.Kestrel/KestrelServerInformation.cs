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
        public KestrelServerInformation(IConfiguration configuration, int threadCount)
        {
            Addresses = GetAddresses(configuration);
            ThreadCount = threadCount;
            NoDelay = true;

            ConfigureStringCache(configuration);
        }

        public ICollection<string> Addresses { get; }

        public int ThreadCount { get; set; }

        public bool NoDelay { get; set; }

        public bool StringCacheOnConnection { get; set; }

        public int StringCacheMaxStrings { get; set; }

        public int StringCacheMaxStringLength { get; set; }

        public IConnectionFilter ConnectionFilter { get; set; }

        private static ICollection<string> GetAddresses(IConfiguration configuration)
        {
            var addresses = new List<string>();

            var urls = configuration["server.urls"];

            if (!string.IsNullOrEmpty(urls))
            {
                addresses.AddRange(urls.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            }

            return addresses;
        }

        private void ConfigureStringCache(IConfiguration configuration)
        {
            bool stringCacheOnConnection;
            if (bool.TryParse(configuration["server.stringCacheOnConnection"], out stringCacheOnConnection))
            {
                StringCacheOnConnection = stringCacheOnConnection;
            }
            else
            {
                StringCacheOnConnection = true;
            }
            int stringCacheMaxStrings;
            if (StringCacheOnConnection && int.TryParse(configuration["server.stringCacheMaxStrings"], out stringCacheMaxStrings))
            {
                if (stringCacheMaxStrings <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(stringCacheMaxStrings),
                        stringCacheMaxStrings,
                        "StringCacheMaxStrings must be positive.");
                }
                StringCacheMaxStrings = stringCacheMaxStrings;
            }
            else
            {
                StringCacheMaxStrings = 25;
            }
            int stringCacheMaxStringLength;
            if (StringCacheOnConnection && int.TryParse(configuration["server.stringCacheMaxStringLength"], out stringCacheMaxStringLength))
            {
                if (stringCacheMaxStringLength <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(stringCacheMaxStringLength),
                        stringCacheMaxStringLength,
                        "StringCacheMaxStringLength must be positive.");
                }
                StringCacheMaxStringLength = stringCacheMaxStringLength;
            }
            else
            {
                StringCacheMaxStringLength = 256;
            }
        }
    }
}
