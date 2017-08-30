﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public class FrameConnectionContext
    {
        public string ConnectionId { get; set; }
        public long FrameConnectionId { get; set; }
        public ServiceContext ServiceContext { get; set; }
        public IFeatureCollection ConnectionFeatures { get; set; }
        public IList<IConnectionAdapter> ConnectionAdapters { get; set; }
        public PipeFactory PipeFactory { get; set; }
        public IPEndPoint LocalEndPoint { get; set; }
        public IPEndPoint RemoteEndPoint { get; set; }
        public IPipeConnection Transport { get; set; }
        public IPipeConnection Application { get; set; }
    }
}
