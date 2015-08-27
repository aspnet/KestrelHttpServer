// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNet.Hosting.Server;
using Microsoft.AspNet.Http.Features;
using Microsoft.Dnx.Runtime;
using Microsoft.Framework.Configuration;
using Microsoft.Framework.Logging;
using Constants = Microsoft.AspNet.Server.Kestrel.Infrastructure.Constants;

namespace Microsoft.AspNet.Server.Kestrel
{
    /// <summary>
    /// Summary description for ServerFactory
    /// </summary>
    public class ServerFactory : IServerFactory
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IApplicationShutdown _appShutdownService;
        
        public ServerFactory(ILibraryManager libraryManager, IApplicationShutdown appShutdownService, ILogger<KestrelEngine> logger)
        {
            _libraryManager = libraryManager;
            _appShutdownService = appShutdownService;
            KestrelTrace.Log = new KestrelTrace(logger);
        }

        public IServerInformation Initialize(IConfiguration configuration)
        {
            var information = new ServerInformation();
            information.Initialize(configuration);
            return information;
        }

        public IDisposable Start(IServerInformation serverInformation, Func<IFeatureCollection, Task> application)
        {
            var disposables = new List<IDisposable>();
            var information = (ServerInformation)serverInformation;
            var engine = new KestrelEngine(_libraryManager, _appShutdownService);
            engine.Start(information.ThreadCount == 0 ? 1 : information.ThreadCount);
            foreach (var address in information.Addresses)
            {
                disposables.Add(engine.CreateServer(
                    address.Scheme,
                    // Unix sockets use a file path, not a hostname.
                    address.Scheme == Constants.UnixScheme ? address.Path : address.Host,
                    address.Port,
                    async frame =>
                    {
                        var request = new ServerRequest(frame);
                        await application.Invoke(request.Features).ConfigureAwait(false);
                    }));
            }
            disposables.Add(engine);
            return new Disposable(() =>
            {
                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            });
        }
    }
}
