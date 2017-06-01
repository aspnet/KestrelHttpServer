// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core
{
    /// <summary>
    /// Configures a timeout for the request body in an HTTP request.
    /// </summary>
    public class RequestBodyTimeout
    {
        /// <summary>
        /// The minimum amount of time allowed for the request body to be entirely read.
        /// </summary>
        public TimeSpan MinimumTime { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// The maximum amount of time allowed for the request body to be entirely read.
        /// Only effective when <see cref="MinimumRate"/> is also set.
        /// </summary>
        public TimeSpan? MaximumTime { get; set; }

        /// <summary>
        /// The minimum incoming data rate in bytes/second that the request should be read at after
        /// <see cref="MinimumTime"/> has elapsed.
        /// </summary>
        public double? MinimumRate { get; set; }
    }
}
