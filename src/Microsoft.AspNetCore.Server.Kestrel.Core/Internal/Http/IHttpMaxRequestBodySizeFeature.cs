// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    /// <summary>
    /// 
    /// </summary>
    public interface IHttpMaxRequestBodySizeFeature
    {
        // TODO: Explain how to override
        /// <summary>
        /// Gets or sets the maximum allowed size of the current request body in bytes.
        /// When set to null, the maximunm request body size is unlimited.
        /// This limit does not affect upgraded connections which are always unlimited.
        /// Overrides <see cref="KestrelServerLimits.MaxRequestBodySize"/>.
        /// </summary>
        /// <remarks>
        /// Defaults to null (unlimited).
        /// </remarks>
        long? MaxRequestBodySize { get; set; }
    }
}
