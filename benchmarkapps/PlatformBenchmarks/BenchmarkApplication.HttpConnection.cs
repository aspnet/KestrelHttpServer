// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace PlatformBenchmarks
{
    public partial class BenchmarkApplication : IHttpConnection
    {
        private static readonly Task<bool> TrueTask = Task.FromResult(true);
        private static readonly Task<bool> FalseTask = Task.FromResult(false);

        private State _state;

        public PipeReader Reader { get; set; }
        public PipeWriter Writer { get; set; }

        private HttpParser<ParsingAdapter> Parser { get; } = new HttpParser<ParsingAdapter>();

        public async Task ExecuteAsync()
        {
            try
            {
                await ProcessRequestsAsync();

                Reader.Complete();
            }
            catch (Exception ex)
            {
                Reader.Complete(ex);
            }
            finally
            {
                Writer.Complete();
            }
        }

        private async Task ProcessRequestsAsync()
        {
            while (true)
            {
                // Request input data
                var task = Reader.ReadAsync();

                if (!task.IsCompleted)
                {
                    // No more data in the input
                    await OnReadCompletedAsync();
                }

                // Wait for input data
                var result = await task;
                var buffer = result.Buffer;

                // Process all requests in input data
                if (await ProcessRequests(ref buffer, result.IsCompleted))
                {
                    // Finished, closing connection
                    break;
                }
            }
        }

        private State ParseHttpRequest(State state, ref ReadOnlySequence<byte> buffer, ref SequencePosition examined)
        {
            SequencePosition consumed = default;

            if (state == State.StartLine)
            {
                if (Parser.ParseRequestLine(new ParsingAdapter(this), buffer, out consumed, out examined))
                {
                    state = State.Headers;
                }

                buffer = buffer.Slice(consumed);
            }

            if (state == State.Headers)
            {
                if (Parser.ParseHeaders(new ParsingAdapter(this), buffer, out consumed, out examined, out int consumedBytes))
                {
                    state = State.Body;
                }

                buffer = buffer.Slice(consumed);
            }

            return state;
        }

        private Task<bool> ProcessRequests(ref ReadOnlySequence<byte> buffer, bool isCompleted)
        {
            var state = _state;
            if (!buffer.IsEmpty)
            {
                if (state == State.RequestStart)
                {
                    state = State.StartLine;
                }

                var examined = buffer.End;
                Task responseTask = null;
                while (true)
                {
                    state = ParseHttpRequest(state, ref buffer, ref examined);

                    if (state == State.Body)
                    {
                        responseTask = ProcessRequestAsync();
                        if (responseTask.IsCompletedSuccessfully)
                        {
                            responseTask = null;
                        }
                        else
                        {
                            // Move out of loop fast path to await the response
                            break;
                        }

                        if (!buffer.IsEmpty)
                        {
                            // More input data to parse
                            state = State.StartLine;
                            continue;
                        }
                    }

                    // No more input or incomplete data, Advance the Reader
                    Reader.AdvanceTo(buffer.Start, examined);
                    break;
                }

                if (responseTask == null)
                {
                    // All requests processed
                    if (state == State.Body)
                    {
                        state = State.RequestStart;
                    }
                }
                else
                {
                    // Await response not yet completed
                    _state = state;
                    return ProcessRequestAsync(responseTask, buffer.Start, examined, isCompleted);
                }
            }

            if (isCompleted)
            {
                if (state != State.RequestStart)
                {
                    ThrowUnexpectedEndOfData();
                }

                // Finished sucessfully
                return TrueTask;
            }

            // Ready for more input
            _state = state;
            return FalseTask;
        }

        private async Task<bool> ProcessRequestAsync(Task currentResponse, SequencePosition consumed, SequencePosition examined, bool isCompleted)
        {
            await currentResponse;

            Reader.AdvanceTo(consumed, examined);

            _state = State.RequestStart;

            return false;
        }

        public void OnHeader(Span<byte> name, Span<byte> value)
        {
        }

        public async ValueTask OnReadCompletedAsync()
        {
            await Writer.FlushAsync();
        }

        private static void ThrowUnexpectedEndOfData()
        {
            throw new InvalidOperationException("Unexpected end of data!");
        }

        private enum State
        {
            RequestStart,
            StartLine,
            Headers,
            Body
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BufferWriter<WriterAdapter> GetWriter(PipeWriter pipeWriter)
            => new BufferWriter<WriterAdapter>(new WriterAdapter(pipeWriter));

        private struct WriterAdapter : IBufferWriter<byte>
        {
            public PipeWriter Writer;

            public WriterAdapter(PipeWriter writer)
                => Writer = writer;

            public void Advance(int count)
                => Writer.Advance(count);

            public Memory<byte> GetMemory(int sizeHint = 0)
                => Writer.GetMemory(sizeHint);

            public Span<byte> GetSpan(int sizeHint = 0)
                => Writer.GetSpan(sizeHint);
        }

        private struct ParsingAdapter : IHttpRequestLineHandler, IHttpHeadersHandler
        {
            public BenchmarkApplication RequestHandler;

            public ParsingAdapter(BenchmarkApplication requestHandler)
                => RequestHandler = requestHandler;

            public void OnHeader(Span<byte> name, Span<byte> value)
                => RequestHandler.OnHeader(name, value);

            public void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
                => RequestHandler.OnStartLine(method, version, target, path, query, customMethod, pathEncoded);
        }
    }

}
