// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core
{
    public class RequestBodyTimeout
    {
        public TimeSpan MinimumTime { get; set; } = TimeSpan.FromMinutes(2);
        public TimeSpan? MaximumTime { get; set; }
        public double? MinimumRate { get; set; }
    }
}
