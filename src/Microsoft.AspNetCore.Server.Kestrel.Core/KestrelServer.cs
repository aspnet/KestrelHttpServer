// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
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

        private readonly ILogger _logger;
        private readonly IServerAddressesFeature _serverAddresses;
        private readonly ITransportFactory _transportFactory;

        private bool _isRunning;
        private DateHeaderValueManager _dateHeaderValueManager;
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
            InternalOptions = new InternalKestrelServerOptions();
            _transportFactory = transportFactory;
            _logger = loggerFactory.CreateLogger("Microsoft.AspNetCore.Server.Kestrel");
            Features = new FeatureCollection();
            _serverAddresses = new ServerAddressesFeature();
            Features.Set(_serverAddresses);
            Features.Set(InternalOptions);
        }

        public IFeatureCollection Features { get; }

        public KestrelServerOptions Options { get; }

        private InternalKestrelServerOptions InternalOptions { get; }

        public void Start<TContext>(IHttpApplication<TContext> application)
        {
            try
            {
                if (!BitConverter.IsLittleEndian)
                {
                    throw new PlatformNotSupportedException("Kestrel does not support big-endian architectures.");
                }

                ValidateOptions();

                if (_isRunning)
                {
                    // The server has already started and/or has not been cleaned up yet
                    throw new InvalidOperationException("Server has already started.");
                }
                _isRunning = true;

                var trace = new KestrelTrace(_logger);

                var systemClock = new SystemClock();
                _dateHeaderValueManager = new DateHeaderValueManager(systemClock);
                var connectionManager = new FrameConnectionManager();
                _heartbeat = new Heartbeat(new IHeartbeatHandler[] { _dateHeaderValueManager, connectionManager }, systemClock, trace);

                IThreadPool threadPool;
                if (InternalOptions.ThreadPoolDispatching)
                {
                    threadPool = new LoggingThreadPool(trace);
                }
                else
                {
                    threadPool = new InlineLoggingThreadPool(trace);
                }

                var serviceContext = new ServiceContext
                {
                    Log = trace,
                    HttpParserFactory = frameParser => new HttpParser<FrameAdapter>(frameParser.Frame.ServiceContext.Log),
                    ThreadPool = threadPool,
                    SystemClock = systemClock,
                    DateHeaderValueManager = _dateHeaderValueManager,
                    ConnectionManager = connectionManager,
                    ServerOptions = Options
                };

                var listenOptions = Options.ListenOptions;
                var hasListenOptions = listenOptions.Any();
                var hasServerAddresses = _serverAddresses.Addresses.Any();

                if (hasListenOptions && hasServerAddresses)
                {
                    var joined = string.Join(", ", _serverAddresses.Addresses);
                    _logger.LogWarning($"Overriding address(es) '{joined}'. Binding to endpoints defined in UseKestrel() instead.");

                    _serverAddresses.Addresses.Clear();
                }
                else if (!hasListenOptions && !hasServerAddresses)
                {
                    _logger.LogDebug($"No listening endpoints were configured. Binding to {Constants.DefaultServerAddress} by default.");

                    // "localhost" for both IPv4 and IPv6 can't be represented as an IPEndPoint.
                    StartLocalhost(ServerAddress.FromUrl(Constants.DefaultServerAddress), serviceContext, application);

                    // If StartLocalhost doesn't throw, there is at least one listener.
                    // The port cannot change for "localhost".
                    _serverAddresses.Addresses.Add(Constants.DefaultServerAddress);

                    return;
                }
                else if (!hasListenOptions)
                {
                    // If no endpoints are configured directly using KestrelServerOptions, use those configured via the IServerAddressesFeature.
                    var copiedAddresses = _serverAddresses.Addresses.ToArray();
                    _serverAddresses.Addresses.Clear();

                    foreach (var address in copiedAddresses)
                    {
                        var parsedAddress = ServerAddress.FromUrl(address);

                        if (parsedAddress.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"HTTPS endpoints can only be configured using {nameof(KestrelServerOptions)}.{nameof(KestrelServerOptions.Listen)}().");
                        }

                        if (!string.IsNullOrEmpty(parsedAddress.PathBase))
                        {
                            throw new InvalidOperationException($"A path base can only be configured using {nameof(IApplicationBuilder)}.UsePathBase().");
                        }

                        if (!string.IsNullOrEmpty(parsedAddress.PathBase))
                        {
                            _logger.LogWarning($"Path base in address {address} is not supported and will be ignored. To specify a path base, use {nameof(IApplicationBuilder)}.UsePathBase().");
                        }

                        if (parsedAddress.IsUnixPipe)
                        {
                            listenOptions.Add(new ListenOptions(parsedAddress.UnixPipePath)
                            {
                                Scheme = parsedAddress.Scheme,
                            });
                        }
                        else
                        {
                            if (string.Equals(parsedAddress.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                            {
                                // "localhost" for both IPv4 and IPv6 can't be represented as an IPEndPoint.
                                StartLocalhost(parsedAddress, serviceContext, application);

                                // If StartLocalhost doesn't throw, there is at least one listener.
                                // The port cannot change for "localhost".
                                _serverAddresses.Addresses.Add(parsedAddress.ToString());
                            }
                            else
                            {
                                // These endPoints will be added later to _serverAddresses.Addresses
                                listenOptions.Add(new ListenOptions(CreateIPEndPoint(parsedAddress))
                                {
                                    Scheme = parsedAddress.Scheme,
                                });
                            }
                        }
                    }
                }

                foreach (var endPoint in listenOptions)
                {
                    var connectionHandler = new ConnectionHandler<TContext>(endPoint, serviceContext, application);
                    var transport = _transportFactory.Create(endPoint, connectionHandler);
                    _transports.Add(transport);

                    try
                    {
                        transport.BindAsync().Wait();
                    }
                    catch (AggregateException ex) when (ex.InnerException is AddressInUseException)
                    {
                        throw new IOException($"Failed to bind to address {endPoint}: address already in use.", ex);
                    }

                    // If requested port was "0", replace with assigned dynamic port.
                    _serverAddresses.Addresses.Add(endPoint.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(0, ex, "Unable to start Kestrel.");
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_transports != null)
            {
                var tasks = new Task[_transports.Count];
                for (int i = 0; i < _transports.Count; i++)
                {
                    tasks[i] = _transports[i].UnbindAsync();
                }
                Task.WaitAll(tasks);

                // TODO: Do transport-agnostic connection management/shutdown.
                for (int i = 0; i < _transports.Count; i++)
                {
                    tasks[i] = _transports[i].StopAsync();
                }
                Task.WaitAll(tasks);
            }

            _heartbeat?.Dispose();
            _dateHeaderValueManager?.Dispose();
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

        private void StartLocalhost<TContext>(ServerAddress parsedAddress, ServiceContext serviceContext, IHttpApplication<TContext> application)
        {
            if (parsedAddress.Port == 0)
            {
                throw new InvalidOperationException("Dynamic port binding is not supported when binding to localhost. You must either bind to 127.0.0.1:0 or [::1]:0, or both.");
            }

            var exceptions = new List<Exception>();

            try
            {
                var ipv4ListenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, parsedAddress.Port))
                {
                    Scheme = parsedAddress.Scheme,
                };

                var connectionHandler = new ConnectionHandler<TContext>(ipv4ListenOptions, serviceContext, application);
                var transport = _transportFactory.Create(ipv4ListenOptions, connectionHandler);
                _transports.Add(transport);
                transport.BindAsync().Wait();
            }
            catch (AggregateException ex) when (ex.InnerException is AddressInUseException)
            {
                throw new IOException($"Failed to bind to address {parsedAddress} on the IPv4 loopback interface: port already in use.", ex);
            }
            catch (AggregateException ex)
            {
                _logger.LogWarning(0, $"Unable to bind to {parsedAddress} on the IPv4 loopback interface: ({ex.Message})");
                exceptions.Add(ex.InnerException);
            }

            try
            {
                var ipv6ListenOptions = new ListenOptions(new IPEndPoint(IPAddress.IPv6Loopback, parsedAddress.Port))
                {
                    Scheme = parsedAddress.Scheme,
                };

                var connectionHandler = new ConnectionHandler<TContext>(ipv6ListenOptions, serviceContext, application);
                var transport = _transportFactory.Create(ipv6ListenOptions, connectionHandler);
                _transports.Add(transport);
                transport.BindAsync().Wait();
            }
            catch (AggregateException ex) when (ex.InnerException is AddressInUseException)
            {
                throw new IOException($"Failed to bind to address {parsedAddress} on the IPv6 loopback interface: port already in use.", ex);
            }
            catch (AggregateException ex)
            {
                _logger.LogWarning(0, $"Unable to bind to {parsedAddress} on the IPv6 loopback interface: ({ex.Message})");
                exceptions.Add(ex.InnerException);
            }

            if (exceptions.Count == 2)
            {
                throw new IOException($"Failed to bind to address {parsedAddress}.", new AggregateException(exceptions));
            }
        }

        /// <summary>
        /// Returns an <see cref="IPEndPoint"/> for the given host an port.
        /// If the host parameter isn't "localhost" or an IP address, use IPAddress.Any.
        /// </summary>
        internal static IPEndPoint CreateIPEndPoint(ServerAddress address)
        {
            IPAddress ip;

            if (!IPAddress.TryParse(address.Host, out ip))
            {
                ip = IPAddress.IPv6Any;
            }

            return new IPEndPoint(ip, address.Port);
        }
    }
}