// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    /// <summary>
    /// Defines a class that provides a pipeline to which data can be written.
    /// </summary>
    public interface IPipeWriter
    {
        /// <summary>
        /// Allocates memory from the pipeline to write into.
        /// </summary>
        /// <param name="minimumSize">The minimum size buffer to allocate</param>
        /// <returns>A <see cref="WritableBuffer"/> that can be written to.</returns>
        WritableBuffer Alloc(int minimumSize = 0);

        /// <summary>
        /// Marks the pipeline as being complete, meaning no more items will be written to it.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the pipeline to complete.</param>
        void Complete(Exception exception = null);

        /// <summary>
        /// Cancel to currently pending or next call to <see cref="ReadAsync"/> if none is pending, without completing the <see cref="IPipeReader"/>.
        /// </summary>
        void CancelPendingFlush();

        /// <summary>
        /// Registers callback that gets executed when reader side of pipe completes.
        /// </summary>
        void OnReaderCompleted(Action<Exception, object> callback, object state);
    }
}
