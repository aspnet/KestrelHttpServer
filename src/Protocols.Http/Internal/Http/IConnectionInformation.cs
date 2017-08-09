// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO.Pipelines;
using System.Net;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public class ConnectionInformation
    {
        public IPEndPoint LocalEndPoint { get; internal set; }
        public IPEndPoint RemoteEndPoint { get; internal set; }
        public PipeFactory PipeFactory { get; internal set; }
    }
}