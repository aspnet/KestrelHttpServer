// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public class EmptyRequestBodyReader : IRequestBodyReader
    {
        public static readonly IRequestBodyReader Instance = new EmptyRequestBodyReader();

        public Task ConsumeAsync(CancellationToken cancellationToken = default(CancellationToken))
            => TaskCache.CompletedTask;

        public Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default(CancellationToken))
            => TaskCache.CompletedTask;

        public Task<int> ReadAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
            => TaskCache<int>.DefaultCompletedTask;

        public void Reset()
        {
        }

        public Task StartAsync(MessageBody messageBody, CancellationToken cancellationToken = default(CancellationToken))
            => TaskCache.CompletedTask;
    }
}
