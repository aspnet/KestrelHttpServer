// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    /// <summary>
    /// Defines a class that provides a pipeline from which data can be read.
    /// </summary>
    public interface IPipeReader
    {
        /// <summary>
        /// Attempt to synchronously read data the <see cref="IPipeReader"/>.
        /// </summary>
        /// <param name="result">The <see cref="ReadResult"/></param>
        /// <returns>True if data was available, or if the call was cancelled or the writer completed with an error.</returns>
        /// <remarks>If the pipe returns false, there's no need to call Advance.</remarks>
        bool TryRead(out ReadResult result);

        /// <summary>
        /// Asynchronously reads a sequence of bytes from the current <see cref="IPipeReader"/>.
        /// </summary>
        /// <returns>A <see cref="ReadableBufferAwaitable"/> representing the asynchronous read operation.</returns>
        ReadableBufferAwaitable ReadAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Moves forward the pipeline's read cursor to after the consumed data.
        /// </summary>
        /// <param name="consumed">Marks the extent of the data that has been succesfully proceesed.</param>
        /// <param name="examined">Marks the extent of the data that has been read and examined.</param>
        /// <remarks>
        /// The memory for the consumed data will be released and no longer available.
        /// The examined data communicates to the pipeline when it should signal more data is available.
        /// </remarks>
        void Advance(ReadCursor consumed, ReadCursor examined);

        /// <summary>
        /// Cancel to currently pending or next call to <see cref="ReadAsync"/> if none is pending, without completing the <see cref="IPipeReader"/>.
        /// </summary>
        void CancelPendingRead();

        /// <summary>
        /// Signal to the producer that the consumer is done reading.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the pipeline to complete.</param>
        void Complete(Exception exception = null);

        /// <summary>
        /// Registers callback that gets executed when writer side of pipe completes.
        /// </summary>
        void OnWriterCompleted(Action<Exception, object> callback, object state);
    }
}
