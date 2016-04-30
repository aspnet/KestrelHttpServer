// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Server.Abstractions
{
    public class ConnectionFilterContext
    {
        public ServerAddress Address { get; set; }
        public Stream Connection { get; set; }
        public Action<IFeatureCollection> PrepareRequest { get; set; }
    }
}
