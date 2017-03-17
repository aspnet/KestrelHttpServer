// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    /// <summary>
    ///   Operations performed for buffered socket output
    /// </summary>
    public interface ISocketOutput
    {
        void Write(ArraySegment<byte> buffer, bool chunk = false);
        Task WriteAsync(ArraySegment<byte> buffer, bool chunk = false, CancellationToken cancellationToken = default(CancellationToken));
        void Flush();
        Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Returns an iterator pointing to the tail of the response buffer. Response data can be appended
        /// manually or by using <see cref="MemoryPoolIterator.CopyFrom(ArraySegment{byte})"/>.
        /// Be careful to ensure all appended blocks are backed by a <see cref="MemoryPoolSlab"/>. 
        /// </summary>
        WritableBuffer Alloc();
    }
}
