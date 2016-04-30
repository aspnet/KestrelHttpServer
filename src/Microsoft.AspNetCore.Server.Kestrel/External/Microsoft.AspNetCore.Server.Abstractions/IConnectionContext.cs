// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;

namespace Microsoft.AspNetCore.Server.Abstractions
{
    public interface IConnectionContext
    {
        SocketInput SocketInput { get; }

        ISocketOutput SocketOutput { get; }

        IConnectionControl ConnectionControl { get;}

        IPEndPoint RemoteEndPoint { get; }

        IPEndPoint LocalEndPoint { get; }

        string ConnectionId { get;  }
        
        ServerAddress ServerAddress { get; }
    }
}