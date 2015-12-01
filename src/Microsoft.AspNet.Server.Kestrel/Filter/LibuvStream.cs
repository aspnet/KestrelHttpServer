// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Http;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNet.Server.Kestrel.Filter
{
    public class LibuvStream : IDuplexStreamAsync<byte>
    {
        private readonly SocketInput _input;
        private readonly ISocketOutput _output;

        public LibuvStream(SocketInput input, ISocketOutput output)
        {
            _input = input;
            _output = output;
        }

        public Task FlushAsync()
            => TaskUtilities.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken)
            => TaskUtilities.CompletedTask;

        public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count)
            => _input.ReadAsync(buffer, offset, count);

        public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _input.ReadAsync(buffer, offset, count);

        public Task WriteAsync(byte[] buffer, int offset, int count)
            => WriteAsync(buffer, offset, count, CancellationToken.None);

        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            ArraySegment<byte> segment;
            if (buffer != null)
            {
                segment = new ArraySegment<byte>(buffer, offset, count);
            }
            else
            {
                segment = default(ArraySegment<byte>);
            }
            return _output.WriteAsync(segment, cancellationToken: token);
        }

        public void Dispose()
        {
        }
    }
}
