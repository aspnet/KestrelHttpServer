// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Infrastructure;
using System;

namespace Microsoft.AspNetCore.Server.Abstractions
{
    public interface IServiceContext
    {
        IApplicationLifetime AppLifetime { get; }

        IConnectionTrace Log { get; }

        IThreadPool ThreadPool { get; }
        Func<IConnectionContext, IFrameControl> FrameFactory { get; }
        DateHeaderValueManager DateHeaderValueManager { get; }
        ServerOptions ServerOptions { get; }

        IHttpComponentFactory HttpComponentFactory { get; }

    }
}