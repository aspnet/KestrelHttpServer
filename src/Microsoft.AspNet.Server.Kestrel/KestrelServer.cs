﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Hosting.Server;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Http.Features;
using Microsoft.AspNet.Server.Kestrel.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNet.Server.Kestrel
{
    public class KestrelServer : IServer
    {
        private Stack<IDisposable> _disposables;
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly ILogger _logger;
        private readonly IHttpContextFactory _httpContextFactory;

        public KestrelServer(IFeatureCollection features, IApplicationLifetime applicationLifetime, ILogger logger, IHttpContextFactory httpContextFactory)
        {
            if (features == null)
            {
                throw new ArgumentNullException(nameof(features));
            }

            if (applicationLifetime == null)
            {
                throw new ArgumentNullException(nameof(applicationLifetime));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            if (httpContextFactory == null)
            {
                throw new ArgumentNullException(nameof(httpContextFactory));
            }

            _applicationLifetime = applicationLifetime;
            _logger = logger;
            Features = features;
            _httpContextFactory = httpContextFactory;
        }

        public IFeatureCollection Features { get; }

        public void Start(RequestDelegate requestDelegate)
        {
            if (_disposables != null)
            {
                // The server has already started and/or has not been cleaned up yet
                throw new InvalidOperationException("Server has already started.");
            }
            _disposables = new Stack<IDisposable>();

            try
            {
                var information = (KestrelServerInformation)Features.Get<IKestrelServerInformation>();
                var dateHeaderValueManager = new DateHeaderValueManager();
                var engine = new KestrelEngine(new ServiceContext
                {
                    AppLifetime = _applicationLifetime,
                    Log = new KestrelTrace(_logger),
                    HttpContextFactory = _httpContextFactory,
                    DateHeaderValueManager = dateHeaderValueManager,
                    ConnectionFilter = information.ConnectionFilter,
                    NoDelay = information.NoDelay
                });

                _disposables.Push(engine);
                _disposables.Push(dateHeaderValueManager);

                var threadCount = GetThreadCount(information);

                engine.Start(threadCount);
                var atLeastOneListener = false;

                foreach (var address in information.Addresses)
                {
                    var parsedAddress = ServerAddress.FromUrl(address);
                    if (parsedAddress == null)
                    {
                        throw new FormatException("Unrecognized listening address: " + address);
                    }
                    else
                    {
                        atLeastOneListener = true;
                        _disposables.Push(engine.CreateServer(
                            parsedAddress,
                            requestDelegate));
                    }
                }

                if (!atLeastOneListener)
                {
                    throw new InvalidOperationException("No recognized listening addresses were configured.");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposables != null)
            {
                while (_disposables.Count > 0)
                {
                    _disposables.Pop().Dispose();
                }
                _disposables = null;
            }
        }

        private static int GetThreadCount(IKestrelServerInformation information)
        {
            int threadCount;
            if (information.ThreadCount.HasValue)
            {
                // ThreadCount has been user set, use that value
                threadCount = information.ThreadCount.Value;

                if (threadCount < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(information.ThreadCount),
                        information.ThreadCount,
                        "ThreadCount cannot be negative");
                }

                return threadCount;
            }

            // Actual core count would be a better number
            // rather than logical cores which includes hyper-threaded cores.
            // Divide by 2 for hyper-threading, and good defaults (still need threads to do webserving).
            // Can be user overriden using IKestrelServerInformation.ThreadCount
            threadCount = Environment.ProcessorCount >> 1;

            if (threadCount < 1)
            {
                // Ensure shifted value is at least one
                return 1;
            }

            if (threadCount > 16)
            {
                // Receive Side Scaling RSS Processor count currently maxes out at 16
                // would be better to check the NIC's current hardware queues; but xplat...
                return 16;
            }

            return threadCount;
        }
    }
}
