// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Adapter;
using Microsoft.AspNetCore.Server.Kestrel.Transport;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class FrameContext
    {
        public string ConnectionId { get; set; }
        public IConnectionInformation ConnectionInformation { get; set; }
        public IEnumerable<IAdaptedConnection> AdaptedConnections { get; set; }
        public ServiceContext ServiceContext { get; set; }

        public IPipeReader Input { get; set; }
        public IPipeWriter Output { get; set; }
    }
}
