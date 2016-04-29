// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Http;
using Microsoft.AspNetCore.Server.Kestrel.Networking;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Server.Kestrel
{

    public class UvOptions
    {
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// The amount of time after the server begins shutting down before connections will be forcefully closed.
        /// By default, Kestrel will wait 5 seconds for any ongoing requests to complete before terminating
        /// the connection.
        /// </summary>
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

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