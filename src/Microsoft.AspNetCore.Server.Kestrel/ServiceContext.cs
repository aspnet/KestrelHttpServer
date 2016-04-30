// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Abstractions;
using Microsoft.AspNetCore.Server.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Http;
using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Server.Kestrel
{
    public class ServiceContext : IServiceContext
    {
        public ServiceContext()
        {
        }

        public ServiceContext(ServiceContext context)
        {
            AppLifetime = context.AppLifetime;
            Log = context.Log;
            ThreadPool = context.ThreadPool;
            FrameFactory = context.FrameFactory;
            DateHeaderValueManager = context.DateHeaderValueManager;
            ServerOptions = context.ServerOptions;
            HttpComponentFactory = context.HttpComponentFactory;
        }

        public IApplicationLifetime AppLifetime { get; set; }

        public IConnectionTrace Log { get; set; }

        public IThreadPool ThreadPool { get; set; }

        public DateHeaderValueManager DateHeaderValueManager { get; set; }

        public KestrelServerOptions ServerOptions { get; set; }

        public IHttpComponentFactory HttpComponentFactory { get; set; }

        public Func<IConnectionContext, Frame> FrameFactory { get; set; }

        public Action<IFeatureCollection> PrepareRequest { get; set; }
    }
}
