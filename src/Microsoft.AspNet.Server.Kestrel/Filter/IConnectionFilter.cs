﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNet.Http.Features;

namespace Microsoft.AspNet.Server.Kestrel.Filter
{
    public interface IConnectionFilter
    {
        Task OnConnection(ConnectionFilterContext context);
        void PrepareRequest(IFeatureCollection features);
    }
}
