// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace PlatformBenchmarks
{
    public static class HttpApplicationConnectionBuilderExtensions
    {
        public static IConnectionBuilder UseHttpApplication<TConnection, TDevirtualizer>(this IConnectionBuilder builder) 
            where TConnection : HttpConnection<TConnection, TDevirtualizer>, new()
            where TDevirtualizer : struct, IParsingDevirtualizer<TConnection, TDevirtualizer>
        {
            return builder.Use(next => new HttpApplication<TConnection, TDevirtualizer>().ExecuteAsync);
        }

        public static IConnectionBuilder UseHttpApplication<TConnection>(this IConnectionBuilder builder)
            where TConnection : HttpConnection<TConnection, NonDevirtualizer<TConnection>>, new()
        {
            return builder.Use(next => new HttpApplication<TConnection, NonDevirtualizer<TConnection>>().ExecuteAsync);
        }
    }

    public class HttpApplication<TConnection, TDevirtualizer> 
        where TConnection : HttpConnection<TConnection, TDevirtualizer>, new()
        where TDevirtualizer : struct, IParsingDevirtualizer<TConnection, TDevirtualizer>
    {
        public Task ExecuteAsync(ConnectionContext connection)
        {
            var parser = new HttpParser<TDevirtualizer>();

            var httpConnection = new TConnection
            {
                Parser = parser,
                Reader = connection.Transport.Input,
                Writer = connection.Transport.Output
            };
            return httpConnection.ExecuteAsync();
        }
    }

    public abstract class HttpConnection<TConnection, TDevirtualizer> : IHttpHeadersHandler, IHttpRequestLineHandler
        where TConnection : HttpConnection<TConnection, TDevirtualizer>, new()
        where TDevirtualizer : struct, IParsingDevirtualizer<TConnection, TDevirtualizer>
    {
        private State _state;

        public PipeReader Reader { get; set; }
        public PipeWriter Writer { get; set; }

        internal HttpParser<TDevirtualizer> Parser { get; set; }

        public virtual void OnHeader(Span<byte> name, Span<byte> value)
        {

        }

        public virtual void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {

        }

        public virtual ValueTask ProcessRequestAsync()
        {
            return default;
        }

        public virtual ValueTask OnReadCompletedAsync()
        {
            return default;
        }

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
                var buffer = result.Buffer;
                var consumed = buffer.Start;
                var examined = buffer.End;

                if (!buffer.IsEmpty)
                {
                    ParseHttpRequest(buffer, out consumed, out examined);

                    if (_state != State.Body && result.IsCompleted)
                    {
                        ThrowUnexpectedEndOfData();
                    }
                }
                else if (result.IsCompleted)
                {
                    break;
                }

                Reader.AdvanceTo(consumed, examined);

                if (_state == State.Body)
                {
                    await ProcessRequestAsync();

                    _state = State.StartLine;
                }
            }
        }

        private void ParseHttpRequest(in ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
        {
            consumed = buffer.Start;
            examined = buffer.End;

            var parsingStartLine = _state == State.StartLine;
            if (parsingStartLine)
            {
                var devirtualizer = new TDevirtualizer() { Connection = (TConnection)this };
                if (Parser.ParseRequestLine(devirtualizer, buffer, out consumed, out examined))
                {
                    _state = State.Headers;
                }
            }

            if (_state == State.Headers)
            {
                var devirtualizer = new TDevirtualizer() { Connection = (TConnection)this };
                if (Parser.ParseHeaders(devirtualizer, parsingStartLine ? buffer.Slice(consumed) : buffer, out consumed, out examined, out int consumedBytes))
                {
                    _state = State.Body;
                }
            }
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

    public struct NonDevirtualizer<TConnection> : IParsingDevirtualizer<TConnection, NonDevirtualizer<TConnection>>
            where TConnection : HttpConnection<TConnection, NonDevirtualizer<TConnection>>, new()
    {
        public TConnection Connection { get; set; }

        public void OnHeader(Span<byte> name, Span<byte> value)
            => Connection.OnHeader(name, value);

        public void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
            => Connection.OnStartLine(method, version, target, path, query, customMethod, pathEncoded);
    }
}
