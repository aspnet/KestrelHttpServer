// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal
{
    public class AdaptedPipeline
    {
        private const int MinAllocBufferSize = 2048;

        private readonly Stream _stream;
        private readonly IKestrelTrace _trace;
        private readonly IPipeWriter _transportOutputPipe;
        private readonly IPipeReader _transportInputPipe;

        public AdaptedPipeline(
            Stream stream,
            IPipeReader transportInputPipe,
            IPipeWriter transportOutputPipe,
            IPipe inputPipe,
            IPipe outputPipe,
            IKestrelTrace trace)
        {
            _stream = stream;
            _transportInputPipe = transportInputPipe;
            _transportOutputPipe = transportOutputPipe;
            Input = inputPipe;
            Output = outputPipe;
            _trace = trace;
        }

        public IPipe Input { get; }

        public IPipe Output { get; }

        public async Task RunAsync()
        {
            var inputTask = ReadInputAsync();
            var outputTask = WriteOutputAsync();

            await inputTask;
            await outputTask;
        }

        private async Task WriteOutputAsync()
        {
            try
            {
                while (true)
                {
                    var readResult = await Output.Reader.ReadAsync();
                    var buffer = readResult.Buffer;

                    try
                    {
                        if (buffer.IsEmpty && readResult.IsCompleted)
                        {
                            break;
                        }

                        if (buffer.IsEmpty)
                        {
                            await _stream.FlushAsync();
                        }
                        else if (buffer.IsSingleSpan)
                        {
                            var array = buffer.First.GetArray();
                            await _stream.WriteAsync(array.Array, array.Offset, array.Count);
                        }
                        else
                        {
                            foreach (var memory in buffer)
                            {
                                var array = memory.GetArray();
                                await _stream.WriteAsync(array.Array, array.Offset, array.Count);
                            }
                        }
                    }
                    finally
                    {
                        Output.Reader.Advance(buffer.End);
                    }
                }

                _transportOutputPipe.Complete();
            }
            catch (Exception ex)
            {
                _transportOutputPipe.Complete(ex);
            }
            finally
            {
                Output.Reader.Complete();
            }
        }

        private async Task ReadInputAsync()
        {
            int bytesRead;

            while (true)
            {
                try
                {
                    var outputBuffer = Input.Writer.Alloc(MinAllocBufferSize);

                    var array = outputBuffer.Buffer.GetArray();
                    try
                    {
                        bytesRead = await _stream.ReadAsync(array.Array, array.Offset, array.Count);
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
                catch (Exception ex)
                {
                    Input.Writer.Complete(ex);

                    // Don't rethrow the exception. It should be handled by the Pipeline consumer.
                    return;
                }
            }

            Input.Writer.Complete();
            // The application could have ended the input pipe so complete
            // the transport pipe as well
            _transportInputPipe.Complete();
        }
    }
}