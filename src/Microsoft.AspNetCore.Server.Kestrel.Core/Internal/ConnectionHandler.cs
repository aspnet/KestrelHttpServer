// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Transport;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal
{
    public class ConnectionHandler<TContext> : IConnectionHandler, IDisposable
    {
        // Base32 encoding - in ascii sort order for easy text based sorting
        private static readonly string _encode32Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUV";

        // Seed the _lastConnectionId for this application instance with
        // the number of 100-nanosecond intervals that have elapsed since 12:00:00 midnight, January 1, 0001
        // for a roughly increasing _requestId over restarts
        private static long _lastConnectionId = DateTime.UtcNow.Ticks;

        private readonly ServiceContext _serviceContext;
        private readonly IHttpApplication<TContext> _application;
        private readonly PipeFactory _pipeFactory;

        public ConnectionHandler(ServiceContext serviceContext, IHttpApplication<TContext> application)
        {
            _serviceContext = serviceContext;
            _application = application;
            _pipeFactory = new PipeFactory();
        }

        public IConnectionContext OnConnection(IConnectionInformation connectionInfo, PipeOptions inputOptions, PipeOptions outputOptions)
        {
            var inputPipe = _pipeFactory.Create(inputOptions);
            var outputPipe = _pipeFactory.Create(outputOptions);

            var connectionId = GenerateConnectionId(Interlocked.Increment(ref _lastConnectionId));

            var frameContext = new FrameContext
            {
                ConnectionId = connectionId,
                ConnectionInformation = connectionInfo,
                ServiceContext = _serviceContext,
                Input = inputPipe.Reader,
                Output = outputPipe.Writer
            };

            var frame = new Frame<TContext>(_application, frameContext);
            frame.Start();

            return new ConnectionContext(frame)
            {
                ConnectionId = connectionId,
                Input = inputPipe.Writer,
                Output = outputPipe.Reader,
            };
        }

        //private void StartFrame()
        //{
        //    if (_connectionAdapters.Count == 0)
        //    {
        //        _frame.Start();
        //    }
        //    else
        //    {
        //        // ApplyConnectionAdaptersAsync should never throw. If it succeeds, it will call _frame.Start().
        //        // Otherwise, it will close the connection.
        //        var ignore = ApplyConnectionAdaptersAsync();
        //    }
        //}

        //private async Task ApplyConnectionAdaptersAsync(ConnectionContext connectionContext, FrameContext frameContext)
        //{
        //    try
        //    {
        //        var rawStream = new RawStream(Input.Reader, Output);
        //        var adapterContext = new ConnectionAdapterContext(rawStream);
        //        var adaptedConnections = new IAdaptedConnection[_connectionAdapters.Count];

        //        for (var i = 0; i < _connectionAdapters.Count; i++)
        //        {
        //            var adaptedConnection = await _connectionAdapters[i].OnConnectionAsync(adapterContext);
        //            adaptedConnections[i] = adaptedConnection;
        //            adapterContext = new ConnectionAdapterContext(adaptedConnection.ConnectionStream);
        //        }

        //        if (adapterContext.ConnectionStream != rawStream)
        //        {
        //            _filteredStream = adapterContext.ConnectionStream;
        //            _adaptedPipeline = new AdaptedPipeline(
        //                adapterContext.ConnectionStream,
        //                Thread.PipelineFactory.Create(ListenerContext.AdaptedPipeOptions),
        //                Thread.PipelineFactory.Create(ListenerContext.AdaptedPipeOptions));

        //            _frame.Input = _adaptedPipeline.Input;
        //            _frame.Output = _adaptedPipeline.Output;

        //            // Don't attempt to read input if connection has already closed.
        //            // This can happen if a client opens a connection and immediately closes it.
        //            _readInputTask = _socketClosedTcs.Task.Status == TaskStatus.WaitingForActivation
        //                ? _adaptedPipeline.StartAsync()
        //                : TaskCache.CompletedTask;
        //        }

        //        _frame.AdaptedConnections = adaptedConnections;
        //        _frame.Start();
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.LogError(0, ex, $"Uncaught exception from the {nameof(IConnectionAdapter.OnConnectionAsync)} method of an {nameof(IConnectionAdapter)}.");
        //        Input.Reader.Complete();
        //        ConnectionControl.End(ProduceEndType.SocketDisconnect);
        //    }
        //}

        public void Dispose()
        {
            _pipeFactory.Dispose();
        }

        private static unsafe string GenerateConnectionId(long id)
        {
            // The following routine is ~310% faster than calling long.ToString() on x64
            // and ~600% faster than calling long.ToString() on x86 in tight loops of 1 million+ iterations
            // See: https://github.com/aspnet/Hosting/pull/385

            // stackalloc to allocate array on stack rather than heap
            char* charBuffer = stackalloc char[13];

            charBuffer[0] = _encode32Chars[(int)(id >> 60) & 31];
            charBuffer[1] = _encode32Chars[(int)(id >> 55) & 31];
            charBuffer[2] = _encode32Chars[(int)(id >> 50) & 31];
            charBuffer[3] = _encode32Chars[(int)(id >> 45) & 31];
            charBuffer[4] = _encode32Chars[(int)(id >> 40) & 31];
            charBuffer[5] = _encode32Chars[(int)(id >> 35) & 31];
            charBuffer[6] = _encode32Chars[(int)(id >> 30) & 31];
            charBuffer[7] = _encode32Chars[(int)(id >> 25) & 31];
            charBuffer[8] = _encode32Chars[(int)(id >> 20) & 31];
            charBuffer[9] = _encode32Chars[(int)(id >> 15) & 31];
            charBuffer[10] = _encode32Chars[(int)(id >> 10) & 31];
            charBuffer[11] = _encode32Chars[(int)(id >> 5) & 31];
            charBuffer[12] = _encode32Chars[(int)id & 31];

            // string ctor overload that takes char*
            return new string(charBuffer, 0, 13);
        }

        private class ConnectionContext : IConnectionContext
        {
            private readonly Frame _frame;

            public ConnectionContext(Frame frame)
            {
                _frame = frame;
            }

            public string ConnectionId { get; set; }
            public IPipeWriter Input { get; set; }
            public IPipeReader Output { get; set; }

            public Task StopAsync()
            {
                return _frame.StopAsync();
            }

            public void Abort(Exception ex)
            {
                _frame.Abort(ex);
            }

            public void SetBadRequestState(RequestRejectionReason reason)
            {
                _frame.SetBadRequestState(reason);
            }

            public Task FrameStartedTask => _frame.FrameStartedTask;
        }
    }
}
