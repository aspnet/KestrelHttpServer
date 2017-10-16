// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    public struct FlushResult
    {
        internal ResultFlags ResultFlags;

        /// <summary>
        /// True if the currrent flush was cancelled
        /// </summary>
        public bool IsCancelled => (ResultFlags & ResultFlags.Cancelled) != 0;

        /// <summary>
        /// True if the <see cref="IPipeWriter"/> is complete
        /// </summary>
        public bool IsCompleted => (ResultFlags & ResultFlags.Completed) != 0;
    }
}
