// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    /// <summary>
    /// Provides programmatic configuration of Socket transport features.
    /// </summary>
    public class SocketTransportOptions
    {
        /// <summary>
        /// Gets or sets a value that determines if Kestrel should dispatch writes to the thread pool
        /// </summary>
        public bool DispatchWritesToThreadPool { get; set; } = true;
    }
}
