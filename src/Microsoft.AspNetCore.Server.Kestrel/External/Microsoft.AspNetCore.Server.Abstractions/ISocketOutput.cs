// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Infrastructure;

namespace Microsoft.AspNetCore.Server.Abstractions
{
    /// <summary>
    ///   Operations performed for buffered socket output
    /// </summary>
    public interface ISocketOutput
    {
        void Write(ArraySegment<byte> buffer, bool chunk = false);
        Task WriteAsync(ArraySegment<byte> buffer, bool chunk = false, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Returns an iterator pointing to the tail of the response buffer. Response data can be appended
        /// manually or by using <see cref="MemoryPoolIterator.CopyFrom(ArraySegment{byte})"/>.
        /// Be careful to ensure all appended blocks are backed by a <see cref="MemoryPoolSlab"/>. 
        /// </summary>
        MemoryPoolIterator ProducingStart();

        /// <summary>
        /// Commits the response data appended to the iterator returned from <see cref="ProducingStart"/>.
        /// All the data up to <paramref name="end"/> will be included in the response.
        /// A write operation isn't guaranteed to be scheduled unless <see cref="Write(ArraySegment{byte}, bool)"/>
        /// or <see cref="WriteAsync(ArraySegment{byte}, bool, CancellationToken)"/> is called afterwards.
        /// </summary>
        /// <param name="end">Points to the end of the committed data.</param>
        void ProducingComplete(MemoryPoolIterator end);
    }
}
