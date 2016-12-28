using System;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Filter;

namespace Microsoft.AspNetCore.Server.Kestrel
{
    /// <summary>
    /// Provides programmatic configuration of Kestrel-specific features.
    /// </summary>
    public class KestrelServerOptions
    {
        /// <summary>
        /// Gets or sets whether the <c>Server</c> header should be included in each response.
        /// </summary>
        /// <remarks>
        /// Defaults to true.
        /// </remarks>
        public bool AddServerHeader { get; set; } = true;

        /// <summary>
        /// Enables the UseKestrel options callback to resolve and use services registered by the application during startup.
        /// Typically initialized by <see cref="Hosting.WebHostBuilderKestrelExtensions.UseKestrel(Hosting.IWebHostBuilder, Action{KestrelServerOptions})"/>.
        /// </summary>
        public IServiceProvider ApplicationServices { get; set; }

        /// <summary>
        /// Gets or sets an <see cref="IConnectionFilter"/> that allows each connection <see cref="System.IO.Stream"/>
        /// to be intercepted and transformed.
        /// Configured by the <c>UseHttps()</c> and <see cref="Hosting.KestrelServerOptionsConnectionLoggingExtensions.UseConnectionLogging(KestrelServerOptions)"/>
        /// extension methods.
        /// </summary>
        /// <remarks>
        /// Defaults to null.
        /// </remarks>
        public IConnectionFilter ConnectionFilter { get; set; }

        /// <summary>
        /// <para>
        /// This property is obsolete and will be removed in a future version.
        /// Use <c>Limits.MaxRequestBufferSize</c> instead.
        /// </para>
        /// <para>
        /// Gets or sets the maximum size of the request buffer.
        /// </para>
        /// </summary>
        /// <remarks>
        /// When set to null, the size of the request buffer is unlimited.
        /// Defaults to 1,048,576 bytes (1 MB).
        /// </remarks>
        [Obsolete("This property is obsolete and will be removed in a future version. Use Limits.MaxRequestBufferSize instead.")]
        public long? MaxRequestBufferSize
        {
            get
            {
                return Limits.MaxRequestBufferSize;
            }
            set
            {
                Limits.MaxRequestBufferSize = value;
            }
        }

        /// <summary>
        /// Provides access to request limit options.
        /// </summary>
        public KestrelServerLimits Limits { get; } = new KestrelServerLimits();

        /// <summary>
        /// Set to false to enable Nagle's algorithm at the socket layer for all connections.
        /// </summary>
        /// <remarks>
        /// Defaults to true.
        /// </remarks>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// The byte threashold below which small writes will be coalesced per request or request pipeline.
        /// This can be set to zero to disable for server or switched off per request using the feature <see cref="IHttpBufferingFeature.DisableResponseBuffering" />
        /// </summary>
        /// <remarks>
        /// Defaults to 2920 bytes; or 2 * (MTU - (IPv4 Header + TCP Header)).
        /// This reduces the number of packets sent over the network; however the data will still be flushed at the end of the requests unless <see cref="NoDelay" /> is set to false.
        /// </remarks>
        public uint ApplicationNagleThreshold { get; set; } = 2 * (1500 - (20 + 20));// 2 * (MTU - (IPv4 Header + TCP Header));

        /// <summary>
        /// The amount of time after the server begins shutting down before connections will be forcefully closed.
        /// Kestrel will wait for the duration of the timeout for any ongoing request processing to complete before
        /// terminating the connection. No new connections or requests will be accepted during this time.
        /// </summary>
        /// <remarks>
        /// Defaults to 5 seconds.
        /// </remarks>
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The number of libuv I/O threads used to process requests.
        /// </summary>
        /// <remarks>
        /// Defaults to half of <see cref="Environment.ProcessorCount" /> rounded down and clamped between 1 and 16.
        /// </remarks>
        public int ThreadCount { get; set; } = ProcessorThreadCount;

        private static int ProcessorThreadCount
        {
            get
            {
                // Actual core count would be a better number
                // rather than logical cores which includes hyper-threaded cores.
                // Divide by 2 for hyper-threading, and good defaults (still need threads to do webserving).
                var threadCount = Environment.ProcessorCount >> 1;

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
}
