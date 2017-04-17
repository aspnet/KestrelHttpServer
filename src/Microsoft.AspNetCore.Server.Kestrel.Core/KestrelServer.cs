// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Server.Kestrel.Core
{
    public class KestrelServer : IServer
    {
        private readonly List<ITransport> _transports = new List<ITransport>();
        private readonly FrameConnectionManager _connectionManager = new FrameConnectionManager();

        private readonly ILogger _logger;
        private readonly KestrelTrace _trace;
        private readonly IServerAddressesFeature _serverAddresses;
        private readonly ITransportFactory _transportFactory;

        private bool _hasStarted;
        private int _stopped;
        private Heartbeat _heartbeat;

        public KestrelServer(
            IOptions<KestrelServerOptions> options,
            ITransportFactory transportFactory,
            ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (transportFactory == null)
            {
                throw new ArgumentNullException(nameof(transportFactory));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            Options = options.Value ?? new KestrelServerOptions();
            _transportFactory = transportFactory;
            _logger = loggerFactory.CreateLogger("Microsoft.AspNetCore.Server.Kestrel");
            _trace = new KestrelTrace(_logger);
            Features = new FeatureCollection();
            _serverAddresses = new ServerAddressesFeature();
            Features.Set(_serverAddresses);
        }

        public IFeatureCollection Features { get; }

        public KestrelServerOptions Options { get; }

        public async Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
        {
            try
            {
                if (!BitConverter.IsLittleEndian)
                {
                    throw new PlatformNotSupportedException("Kestrel does not support big-endian architectures.");
                }

                ValidateOptions();

                if (_hasStarted)
                {
                    // The server has already started and/or has not been cleaned up yet
                    throw new InvalidOperationException("Server has already started.");
                }
                _hasStarted = true;

                var systemClock = new SystemClock();
                var dateHeaderValueManager = new DateHeaderValueManager(systemClock);
                _heartbeat = new Heartbeat(new IHeartbeatHandler[] { dateHeaderValueManager, _connectionManager }, systemClock, _trace);

                IThreadPool threadPool;
                if (Options.UseTransportThread)
                {
                    threadPool = new InlineLoggingThreadPool(_trace);
                }
                else
                {
                    threadPool = new LoggingThreadPool(_trace);
                }

                var serviceContext = new ServiceContext
                {
                    Log = _trace,
                    HttpParserFactory = frameParser => new HttpParser<FrameAdapter>(frameParser.Frame.ServiceContext.Log),
                    ThreadPool = threadPool,
                    SystemClock = systemClock,
                    DateHeaderValueManager = dateHeaderValueManager,
                    ConnectionManager = _connectionManager,
                    ServerOptions = Options
                };

                async Task OnBind(ListenOptions endpoint)
                {
                    var connectionHandler = new ConnectionHandler<TContext>(endpoint, serviceContext, application);
                    var transport = _transportFactory.Create(endpoint, connectionHandler);
                    _transports.Add(transport);

                    await transport.BindAsync().ConfigureAwait(false);
                }

                await AddressBinder.BindAsync(_serverAddresses, Options.ListenOptions, _logger, OnBind).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(0, ex, "Unable to start Kestrel.");
                Dispose();
                throw;
            }
        }

        // Graceful shutdown if possible
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 1)
            {
                return;
            }

            var tasks = new Task[_transports.Count];
            for (int i = 0; i < _transports.Count; i++)
            {
                tasks[i] = _transports[i].UnbindAsync();
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);

            if (!await _connectionManager.CloseAllConnectionsAsync(cancellationToken).ConfigureAwait(false))
            {
                _trace.NotAllConnectionsClosedGracefully();

                if (!await _connectionManager.AbortAllConnectionsAsync().ConfigureAwait(false))
                {
                    _trace.NotAllConnectionsAborted();
                }
            }

            for (int i = 0; i < _transports.Count; i++)
            {
                tasks[i] = _transports[i].StopAsync();
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);

            _heartbeat?.Dispose();
        }

        // Ungraceful shutdown
        public void Dispose()
        {
            var cancelledTokenSource = new CancellationTokenSource();
            cancelledTokenSource.Cancel();
            StopAsync(cancelledTokenSource.Token).GetAwaiter().GetResult();
        }

        private void ValidateOptions()
        {
            if (Options.Limits.MaxRequestBufferSize.HasValue &&
                Options.Limits.MaxRequestBufferSize < Options.Limits.MaxRequestLineSize)
            {
                throw new InvalidOperationException(
                    $"Maximum request buffer size ({Options.Limits.MaxRequestBufferSize.Value}) must be greater than or equal to maximum request line size ({Options.Limits.MaxRequestLineSize}).");
            }

            if (Options.Limits.MaxRequestBufferSize.HasValue &&
                Options.Limits.MaxRequestBufferSize < Options.Limits.MaxRequestHeadersTotalSize)
            {
                throw new InvalidOperationException(
                    $"Maximum request buffer size ({Options.Limits.MaxRequestBufferSize.Value}) must be greater than or equal to maximum request headers size ({Options.Limits.MaxRequestHeadersTotalSize}).");
            }
        }
    }
}