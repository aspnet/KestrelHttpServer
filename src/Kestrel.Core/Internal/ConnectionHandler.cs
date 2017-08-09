// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public class ConnectionHandler : IConnectionHandler
    {
        private readonly ListenOptions _listenOptions;
        private readonly ConnectionDelegate _connectionDelegate;

        public ConnectionHandler(ListenOptions listenOptions)
        {
            _listenOptions = listenOptions;

            // Build the pipeline
            _connectionDelegate = listenOptions.Build();
        }

        public IConnectionContext OnConnection(IConnectionInformation connectionInfo)
        {
            var connection = new Connection(connectionInfo);

            // Since data cannot be added to the inputPipe by the transport until OnConnection returns,
            // Frame.ProcessRequestsAsync is guaranteed to unblock the transport thread before calling
            // application code.
            _ = _connectionDelegate(connection);

            return connection;
        }

        private class Connection : ConnectionContext, IConnectionContext, IPipe
        {
            private IConnectionInformation _connectionInfo;
            private IPipe _inputPipe;
            private IPipe _outputPipe;

            public Connection(IConnectionInformation connectionInfo)
            {
                _connectionInfo = connectionInfo;

                _inputPipe = connectionInfo.PipeFactory.Create(GetInputPipeOptions(connectionInfo.InputWriterScheduler));
                _outputPipe = connectionInfo.PipeFactory.Create(GetOutputPipeOptions(connectionInfo.OutputReaderScheduler));
                ConnectionId = CorrelationIdGenerator.GetNextId();
                Transport = this;
            }

            public override string ConnectionId { get; }

            public override IFeatureCollection Features { get; } = new FeatureCollection();

            public override IPipe Transport { get; set; }

            IPipeReader IPipe.Reader => _inputPipe.Reader;

            IPipeWriter IPipe.Writer => _outputPipe.Writer;

            IPipeWriter IConnectionContext.Input => _inputPipe.Writer;

            IPipeReader IConnectionContext.Output => _outputPipe.Reader;

            public void Abort(Exception ex)
            {

            }

            public void OnConnectionClosed(Exception ex)
            {
            }

            public void Reset()
            {
            }


            // Internal for testing
            internal PipeOptions GetInputPipeOptions(IScheduler writerScheduler) => new PipeOptions
            {
                // ReaderScheduler = _serviceContext.ThreadPool,
                WriterScheduler = writerScheduler,
                // MaximumSizeHigh = _serviceContext.ServerOptions.Limits.MaxRequestBufferSize ?? 0,
                // MaximumSizeLow = _serviceContext.ServerOptions.Limits.MaxRequestBufferSize ?? 0
            };

            internal PipeOptions GetOutputPipeOptions(IScheduler readerScheduler) => new PipeOptions
            {
                ReaderScheduler = readerScheduler,
                // WriterScheduler = _serviceContext.ThreadPool,
                // MaximumSizeHigh = GetOutputResponseBufferSize(),
                // MaximumSizeLow = GetOutputResponseBufferSize()
            };

            private long GetOutputResponseBufferSize()
            {
                int? bufferSize = 0; //_serviceContext.ServerOptions.Limits.MaxResponseBufferSize;
                if (bufferSize == 0)
                {
                    // 0 = no buffering so we need to configure the pipe so the the writer waits on the reader directly
                    return 1;
                }

                // null means that we have no back pressure
                return bufferSize ?? 0;
            }

        }
    }
}
