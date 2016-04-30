// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Http;
using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;
using Microsoft.AspNetCore.Server.Abstractions;
using Microsoft.AspNetCore.Server.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel
{
    public interface IServiceContext
    {
        IApplicationLifetime AppLifetime { get; }

        IConnectionTrace Log { get; }

        IThreadPool ThreadPool { get; }

        Func<IConnectionContext, Frame> FrameFactory { get; }

        DateHeaderValueManager DateHeaderValueManager { get; }

        KestrelServerOptions ServerOptions { get; }

        IHttpComponentFactory HttpComponentFactory { get; }

        Action<IFeatureCollection> PrepareRequest { get; }
    }
}