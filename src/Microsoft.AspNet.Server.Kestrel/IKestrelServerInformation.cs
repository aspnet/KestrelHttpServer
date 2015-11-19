// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNet.Server.Kestrel.Filter;

namespace Microsoft.AspNet.Server.Kestrel
{
    public interface IKestrelServerInformation
    {
        int ThreadCount { get; set; }

        bool NoDelay { get; set; }

        bool StringCacheOnConnection { get; set; }

        int StringCacheMaxStrings { get; set; }

        int StringCacheMaxStringLength { get; set; }

        IConnectionFilter ConnectionFilter { get; set; }
    }
}
