// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    public class SocketTransportOptions
    {
        /// <summary>
        /// The number of I/O loops used to process requests.
        /// </summary>
        /// <remarks>
        /// Defaults to half of <see cref="Environment.ProcessorCount" /> rounded down and clamped between 1 and 16.
        /// </remarks>
        //public int IOLoopCountCount { get; set; } = ProcessorThreadCount;
        public int IOLoopCountCount { get; set; } = Math.Min(Environment.ProcessorCount, 16);
    }
}
