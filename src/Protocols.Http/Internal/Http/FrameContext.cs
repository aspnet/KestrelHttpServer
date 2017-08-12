﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public class FrameContext
    {
        public string ConnectionId { get; set; }
        public ServiceContext ServiceContext { get; set; }
        public ConnectionInformation ConnectionInformation { get; set; }
        public ITimeoutControl TimeoutControl { get; set; }
        public IPipeReader Input { get; set; }
        public IPipe Output { get; set; }
    }
}
