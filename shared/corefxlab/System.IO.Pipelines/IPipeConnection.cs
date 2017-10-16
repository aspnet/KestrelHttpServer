// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    /// <summary>
    /// Defines a class that provides a connection from which data can be read from and written to.
    /// </summary>
    public interface IPipeConnection : IDisposable
    {
        /// <summary>
        /// Gets the <see cref="IPipeReader"/> half of the duplex connection.
        /// </summary>
        IPipeReader Input { get; }

        /// <summary>
        /// Gets the <see cref="IPipeWriter"/> half of the duplex connection.
        /// </summary>
        IPipeWriter Output { get; }
    }
}
