// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http.Features;
using System;
using System.Net;

namespace Microsoft.AspNetCore.Server.Abstractions
{
    public interface IConnectionContext : IServiceContext
    {
        SocketInput SocketInput { get; }

        ISocketOutput SocketOutput { get; }

        IConnectionControl ConnectionControl { get;}

        IPEndPoint RemoteEndPoint { get; }

        IPEndPoint LocalEndPoint { get; }

        string ConnectionId { get; }

        Action<IFeatureCollection> PrepareRequest { get; }

        ServerAddress ServerAddress { get; }
    }
}