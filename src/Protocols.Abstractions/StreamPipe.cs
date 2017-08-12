// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Protocols.Abstractions
{
    public class StreamPipe : IPipe
    {
        private const int MinAllocBufferSize = 2048;

        public StreamPipe(PipeFactory pipeFactory)
        {
            Input = pipeFactory.Create();
            Output = pipeFactory.Create();
        }

        public IPipe Input { get; }

        public IPipe Output { get; }

        IPipeReader IPipe.Reader => Input.Reader;

        IPipeWriter IPipe.Writer => Input.Writer;

        public async Task CopyFromAsync(Stream stream)
        {
            var inputTask = ReadInputAsync(stream);
            var outputTask = WriteOutputAsync(stream);

            await inputTask;
            await outputTask;
        }

        private async Task WriteOutputAsync(Stream stream)
        {
            Exception error = null;

            try
            {
                if (stream == null)
                {
                    return;
                }

                while (true)
                {
                    var result = await Output.Reader.ReadAsync();
                    var buffer = result.Buffer;

                    try
                    {
                        if (result.IsCancelled)
                        {
                            break;
                        }

                        if (buffer.IsEmpty)
                        {
                            if (result.IsCompleted)
                            {
                                break;
                            }
                            await stream.FlushAsync();
                        }
                        else if (buffer.IsSingleSpan)
                        {
                            var array = GetArray(buffer.First);
                            await stream.WriteAsync(array.Array, array.Offset, array.Count);
                        }
                        else
                        {
                            foreach (var memory in buffer)
                            {
                                var array = GetArray(memory);
                                await stream.WriteAsync(array.Array, array.Offset, array.Count);
                            }
                        }
                    }
                    finally
                    {
                        Output.Reader.Advance(buffer.End);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                Output.Reader.Complete();
            }
        }

        private async Task ReadInputAsync(Stream stream)
        {
            Exception error = null;

            try
            {
                if (stream == null)
                {
                    // REVIEW: Do we need an exception here?
                    return;
                }

                while (true)
                {

                    var outputBuffer = Input.Writer.Alloc(MinAllocBufferSize);

                    var array = GetArray(outputBuffer.Buffer);
                    try
                    {
                        var bytesRead = await stream.ReadAsync(array.Array, array.Offset, array.Count);
                        outputBuffer.Advance(bytesRead);

                        if (bytesRead == 0)
                        {
                            // FIN
                            break;
                        }
                    }
                    finally
                    {
                        outputBuffer.Commit();
                    }

                    var result = await outputBuffer.FlushAsync();

                    if (result.IsCompleted)
                    {
                        break;
                    }

                }
            }
            catch (Exception ex)
            {
                // Don't rethrow the exception. It should be handled by the Pipeline consumer.
                error = ex;
            }
            finally
            {
                Input.Writer.Complete(error);
                // The application could have ended the input pipe so complete
                // the transport pipe as well
            }
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public static ArraySegment<byte> GetArray(Buffer<byte> buffer)
        {
            ArraySegment<byte> result;
            if (!buffer.TryGetArray(out result))
            {
                throw new InvalidOperationException("Buffer backed by array was expected");
            }
            return result;
        }
    }
}