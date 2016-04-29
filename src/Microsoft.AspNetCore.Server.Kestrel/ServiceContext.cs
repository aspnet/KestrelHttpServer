// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Http;
using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel
{
    public interface IServiceContext
    {
        IApplicationLifetime AppLifetime { get; }

        IKestrelTrace Log { get; }

        IThreadPool ThreadPool { get; }

        Func<IConnectionContext, Frame> FrameFactory { get; }

        DateHeaderValueManager DateHeaderValueManager { get; }

        KestrelServerOptions ServerOptions { get; }

        IHttpComponentFactory HttpComponentFactory { get; }
    }
    
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

        public IKestrelTrace Log { get; set; }

        public IThreadPool ThreadPool { get; set; }

        public Func<IConnectionContext, Frame> FrameFactory { get; set; }

        public DateHeaderValueManager DateHeaderValueManager { get; set; }

        public KestrelServerOptions ServerOptions { get; set; }

        public IHttpComponentFactory HttpComponentFactory { get; set; }
    }
}
