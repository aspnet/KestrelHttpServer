// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using MemoryPool = Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure.MemoryPool;

namespace Microsoft.AspNetCore.Server.Kestrel.Filter.Internal
{
    public class FilteredStreamAdapter : IDisposable
    {
        private readonly Stream _filteredStream;

        public FilteredStreamAdapter(
            string connectionId,
            Stream filteredStream,
            PipelineFactory pipelineFactory,
            MemoryPool memory,
            IKestrelTrace logger)
        {
            SocketInput = pipelineFactory.Create();
            SocketOutput = new StreamSocketOutput(connectionId, filteredStream, memory, logger);

            _filteredStream = filteredStream;
        }

        public Pipe SocketInput { get; }

        public ISocketOutput SocketOutput { get; }

        public void Dispose()
        {
        }

        public async Task ReadInputAsync()
        {
            int bytesRead;

            do
            {
                var block = SocketInput.Alloc();

                try
                {
                    ArraySegment<byte> array;
                    block.Memory.TryGetArray(out array);
                    bytesRead = await _filteredStream.ReadAsync(array.Array, array.Offset, array.Count);
                }
                catch (Exception ex)
                {
                    SocketInput.CompleteWriter(ex);
                    throw;
                }

                SocketInput.AdvanceWriter(bytesRead);
            } while (bytesRead != 0);
        }
    }
}
