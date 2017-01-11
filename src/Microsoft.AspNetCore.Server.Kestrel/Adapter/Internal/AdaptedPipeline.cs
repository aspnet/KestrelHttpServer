// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using MemoryPool = Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure.MemoryPool;

namespace Microsoft.AspNetCore.Server.Kestrel.Adapter.Internal
{
    public class AdaptedPipeline : IDisposable
    {
        private readonly Stream _filteredStream;

        public AdaptedPipeline(
            string connectionId,
            Stream filteredStream,
            PipelineFactory pipelineFactory,
            MemoryPool memory,
            IKestrelTrace logger)
        {
            Input = pipelineFactory.Create();
            Output = new StreamSocketOutput(connectionId, filteredStream, memory, logger);

            _filteredStream = filteredStream;
        }

        public Pipe Input { get; }

        public ISocketOutput Output { get; }

        public void Dispose()
        {
            Input.CompleteWriter();
            Input.CompleteReader();
        }

        public async Task ReadInputAsync()
        {
            int bytesRead;

            do
            {
                var block = Input.Alloc(2048);

                try
                {
                    ArraySegment<byte> array;
                    block.Memory.TryGetArray(out array);
                    bytesRead = await _filteredStream.ReadAsync(array.Array, array.Offset, array.Count);
                    block.Advance(bytesRead);
                    await block.FlushAsync();
                }
                catch (Exception ex)
                {
                    Input.CompleteWriter(ex);
                    throw;
                }
            } while (bytesRead != 0);
        }
    }
}
