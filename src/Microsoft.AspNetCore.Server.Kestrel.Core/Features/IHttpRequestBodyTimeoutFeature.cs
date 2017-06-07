// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Features
{
    /// <summary>
    /// Represents a timeout for the request body in an HTTP request.
    /// </summary>
    public interface IHttpRequestBodyTimeoutFeature
    {
        /// <summary>
        /// The maximum amount of time in which the request body should be fully received.
        /// This limit has no effect on upgraded connections which are always unlimited.
        /// </summary>
        TimeSpan Timeout { get; set; }

        /// <summary>
        /// The minimum data rate in bytes/second at which the request body should be received.
        /// Setting this property to null indicates no minimum data rate should be enforced.
        /// This limit has no effect on upgraded connections which are always unlimited.
        /// </summary>
        MinimumDataRate MinimumDataRate { get; set; }
    }
}
