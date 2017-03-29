// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class ConnectionContext
    {
        public ConnectionContext()
        {
        }

        public ConnectionContext(ListenerContext context)
        {
            ListenerContext = context;
        }

        public ListenerContext ListenerContext { get; set; }

        public IPipe Input { get; set; }

        public ISocketOutput Output { get; set; }

        public IConnectionControl ConnectionControl { get; set; }

        public IPEndPoint RemoteEndPoint { get; set; }

        public IPEndPoint LocalEndPoint { get; set; }

        public string ConnectionId { get; set; }
    }
}