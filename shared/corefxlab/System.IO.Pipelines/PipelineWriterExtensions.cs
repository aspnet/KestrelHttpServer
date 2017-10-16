// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    public static class PipelineWriterExtensions
    {
        private readonly static Task _completedTask = Task.FromResult(0);

        public static Task WriteAsync(this IPipeWriter output, byte[] source)
        {
            return WriteAsync(output, new ArraySegment<byte>(source));
        }

        public static Task WriteAsync(this IPipeWriter output, ArraySegment<byte> source)
        {
            var writeBuffer = output.Alloc();
            writeBuffer.Write(source);
            return FlushAsync(writeBuffer);
        }

        private static Task FlushAsync(WritableBuffer writeBuffer)
        {
            var awaitable = writeBuffer.FlushAsync();
            if (awaitable.IsCompleted)
            {
                awaitable.GetResult();
                return _completedTask;
            }

            return FlushAsyncAwaited(awaitable);
        }

        private static async Task FlushAsyncAwaited(WritableBufferAwaitable awaitable)
        {
            await awaitable;
        }
    }
}