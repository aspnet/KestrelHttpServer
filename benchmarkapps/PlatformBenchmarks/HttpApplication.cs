// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace PlatformBenchmarks
{
    public static class HttpApplicationConnectionBuilderExtensions
    {
        public static IConnectionBuilder UseHttpApplication<TConnection>(this IConnectionBuilder builder) where TConnection : IHttpConnection, new()
        {
            return builder.Use(next => new HttpApplication<TConnection>().ExecuteAsync);
        }
    }

    public class HttpApplication<TConnection> where TConnection : IHttpConnection, new()
    {
        public Task ExecuteAsync(ConnectionContext connection)
        {
            var parser = new HttpParser<ParsingAdapter>();

            var httpConnection = new TConnection
            {
                Reader = connection.Transport.Input,
                Writer = connection.Transport.Output
            };
            return httpConnection.ExecuteAsync();
        }
    }
    public interface IHttpConnection
    {
        PipeReader Reader { get; set; }
        PipeWriter Writer { get; set; }
        Task ExecuteAsync();
    }

    public partial class BenchmarkApplication : IHttpHeadersHandler, IHttpRequestLineHandler, IHttpConnection
    {
        private State _state;

        public PipeReader Reader { get; set; }
        public PipeWriter Writer { get; set; }

        internal HttpParser<ParsingAdapter> Parser { get; set; } = new HttpParser<ParsingAdapter>();

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
                var task = Reader.ReadAsync();

                if (!task.IsCompleted)
                {
                    // No more data in the input
                    await OnReadCompletedAsync();
                }

                var result = await task;
                if (!ParseHttpRequest(ref result))
                {
                    break;
                }

                if (_state == State.Body)
                {
                    await ProcessRequestAsync();

                    _state = State.StartLine;
                }
            }
        }

        // Should be `in` but ReadResult isn't readonly struct
        private bool ParseHttpRequest(ref ReadResult result)
        {
            var buffer = result.Buffer;
            var consumed = buffer.Start;
            var examined = buffer.End;

            if (!buffer.IsEmpty)
            {
                var parsingStartLine = _state == State.StartLine;
                if (parsingStartLine)
                {
                    if (Parser.ParseRequestLine(new ParsingAdapter(this), buffer, out consumed, out examined))
                    {
                        _state = State.Headers;
                    }
                }

                if (_state == State.Headers)
                {
                    if (Parser.ParseHeaders(new ParsingAdapter(this), parsingStartLine ? buffer.Slice(consumed) : buffer, out consumed, out examined, out int consumedBytes))
                    {
                        _state = State.Body;
                    }
                }

                if (_state != State.Body && result.IsCompleted)
                {
                    ThrowUnexpectedEndOfData();
                }
            }
            else if (result.IsCompleted)
            {
                return false;
            }

            Reader.AdvanceTo(consumed, examined);
            return true;
        }

        private static void ThrowUnexpectedEndOfData()
        {
            throw new InvalidOperationException("Unexpected end of data!");
        }

        private enum State
        {
            StartLine,
            Headers,
            Body
        }
    }

    public struct ParsingAdapter : IHttpRequestLineHandler, IHttpHeadersHandler
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
