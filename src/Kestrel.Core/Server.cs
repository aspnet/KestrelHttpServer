// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Server.Kestrel.Core
{
    public class Server
    {
        private readonly List<ITransport> _transports = new List<ITransport>();
        // private readonly Heartbeat _heartbeat;
        private readonly ITransportFactory _transportFactory;

        private bool _hasStarted;
        private int _stopping;
        private readonly TaskCompletionSource<object> _stoppedTcs = new TaskCompletionSource<object>();

        // REVIEW: Does this need to be DI friendly or do we have another layer?
        public Server(List<ListenOptions> bindings, ITransportFactory transportFactory, ILoggerFactory loggerFactory)
        {
            Bindings = bindings;
            _transportFactory = transportFactory;
            Trace = loggerFactory.CreateLogger<Server>();
        }

        public List<ListenOptions> Bindings { get; }

        private ILogger Trace { get; }

        public async Task StartAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                if (!BitConverter.IsLittleEndian)
                {
                    throw new PlatformNotSupportedException(CoreStrings.BigEndianNotSupported);
                }

                // ValidateOptions();

                if (_hasStarted)
                {
                    // The server has already started and/or has not been cleaned up yet
                    throw new InvalidOperationException(CoreStrings.ServerAlreadyStarted);
                }
                _hasStarted = true;
                // _heartbeat.Start();

                async Task OnBind(ListenOptions endpoint)
                {
                    var connectionHandler = new ConnectionHandler(endpoint);
                    var transport = _transportFactory.Create(endpoint, connectionHandler);
                    _transports.Add(transport);

                    await transport.BindAsync().ConfigureAwait(false);
                }

                await AddressBinder.BindAsync(new List<string>(), preferHostingUrls: false, listenOptions: Bindings, logger: Trace, createBinding: OnBind).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.LogCritical(0, ex, "Unable to start Kestrel.");
                Dispose();
                throw;
            }
        }

        // Graceful shutdown if possible
        public async Task StopAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 1)
            {
                await _stoppedTcs.Task.ConfigureAwait(false);
                return;
            }

            try
            {
                var tasks = new Task[_transports.Count];
                for (int i = 0; i < _transports.Count; i++)
                {
                    tasks[i] = _transports[i].UnbindAsync();
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);

                // TODO: We should do this by default
                //if (!await ConnectionManager.CloseAllConnectionsAsync(cancellationToken).ConfigureAwait(false))
                //{
                //    Trace.NotAllConnectionsClosedGracefully();

                //    if (!await ConnectionManager.AbortAllConnectionsAsync().ConfigureAwait(false))
                //    {
                //        Trace.NotAllConnectionsAborted();
                //    }
                //}

                for (int i = 0; i < _transports.Count; i++)
                {
                    tasks[i] = _transports[i].StopAsync();
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);

                // _heartbeat.Dispose();
            }
            catch (Exception ex)
            {
                _stoppedTcs.TrySetException(ex);
                throw;
            }

            _stoppedTcs.TrySetResult(null);
        }

        // Ungraceful shutdown
        public void Dispose()
        {
            var cancelledTokenSource = new CancellationTokenSource();
            cancelledTokenSource.Cancel();
            StopAsync(cancelledTokenSource.Token).GetAwaiter().GetResult();
        }
    }
}
