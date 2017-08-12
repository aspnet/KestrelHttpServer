// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols.Abstractions.Features;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal
{
    public class LibuvConnectionContext : ConnectionContext, IPipe, IConnectionTransportFeature, IConnectionIdFeature
    {
        public LibuvConnectionContext()
        {
        }

        public LibuvConnectionContext(ListenerContext context)
        {
            ListenerContext = context;
            Input = PipeFactory.Create();
            Output = PipeFactory.Create();
            Transport = this;

            Features.Set<IConnectionIdFeature>(this);
            Features.Set<IConnectionTransportFeature>(this);
        }

        public ListenerContext ListenerContext { get; set; }

        public IPEndPoint RemoteEndPoint { get; set; }
        public IPEndPoint LocalEndPoint { get; set; }

        public PipeFactory PipeFactory => ListenerContext.Thread.PipeFactory;
        public IScheduler InputWriterScheduler => ListenerContext.Thread;
        public IScheduler OutputReaderScheduler => ListenerContext.Thread;

        public IPipe Input { get; }
        public IPipe Output { get; }

        public override string ConnectionId => Features.Get<IConnectionIdFeature>()?.ConnectionId;

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override IPipe Transport { get; set; }

        IPipeReader IPipe.Reader => Input.Reader;

        IPipeWriter IPipe.Writer => Output.Writer;

        public void Reset()
        {
            // REVIEW: This is broken
        }
    }
}