// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2ConnectionContext
    {
        public string ConnectionId { get; set; }
        public ServiceContext ServiceContext { get; set; }
        public IConnectionInformation ConnectionInformation { get; set; }

        public IPipeReader Input { get; set; }
        public IPipe Output { get; set; }
    }
}
