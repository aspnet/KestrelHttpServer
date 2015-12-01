// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNet.Server.Kestrel.Filter
{
    internal class LoggingStream : IDuplexStreamAsync<byte>
    {
        private readonly IDuplexStreamAsync<byte> _inner;
        private readonly ILogger _logger;

        public LoggingStream(IDuplexStreamAsync<byte> inner, ILogger logger)
        {
            _inner = inner;
            _logger = logger;
        }

        public Task FlushAsync()
            => FlushAsync(CancellationToken.None);

        public Task FlushAsync(CancellationToken cancellationToken)
            => _inner.FlushAsync(cancellationToken);

        public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count, CancellationToken.None);

        public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Log("ReadAsync", count, buffer, offset);
            return _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public Task WriteAsync(byte[] buffer, int offset, int count)
            => WriteAsync(buffer, offset, count);

        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Log("WriteAsync", count, buffer, offset);
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public void Dispose()
            => _inner.Dispose();

        private void Log(string method, int count, byte[] buffer, int offset)
        {
            var builder = new StringBuilder($"{method}[{count}] ");

            // Write the hex
            for (int i = offset; i < offset + count; i++)
            {
                builder.Append(buffer[i].ToString("X2"));
                builder.Append(" ");
            }
            builder.AppendLine();
            // Write the bytes as if they were ASCII
            for (int i = offset; i < offset + count; i++)
            {
                builder.Append((char)buffer[i]);
            }

            _logger.LogVerbose(builder.ToString());
        }
    }
}
