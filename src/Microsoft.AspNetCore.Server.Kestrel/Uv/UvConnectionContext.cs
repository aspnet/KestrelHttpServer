// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    public interface IConnectionContext : IServiceContext
    {
        
        SocketInput SocketInput { get; }

        ISocketOutput SocketOutput { get; }

        IConnectionControl ConnectionControl { get;}

        IPEndPoint RemoteEndPoint { get; }

        IPEndPoint LocalEndPoint { get; }

        string ConnectionId { get;  }

        Action<IFeatureCollection> PrepareRequest { get; }
        
        ServerAddress ServerAddress { get; }
    }
    
    public class UvConnectionContext : UvListenerContext, IConnectionContext
    {
        public UvConnectionContext()
        {
        }

        public UvConnectionContext(UvListenerContext context) : base(context)
        {
        }

        public UvConnectionContext(UvConnectionContext context) : base(context)
        {
            SocketInput = context.SocketInput;
            SocketOutput = context.SocketOutput;
            ConnectionControl = context.ConnectionControl;
            RemoteEndPoint = context.RemoteEndPoint;
            LocalEndPoint = context.LocalEndPoint;
            ConnectionId = context.ConnectionId;
            PrepareRequest = context.PrepareRequest;
        }

        public SocketInput SocketInput { get; set; }

        public ISocketOutput SocketOutput { get; set; }

        public IConnectionControl ConnectionControl { get; set; }

        public IPEndPoint RemoteEndPoint { get; set; }

        public IPEndPoint LocalEndPoint { get; set; }

        public string ConnectionId { get; set; }

        public Action<IFeatureCollection> PrepareRequest { get; set; }
    }
}