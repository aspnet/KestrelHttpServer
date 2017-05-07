// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public class FrameConnection : IConnectionContext, ITimeoutControl
    {
        private readonly FrameConnectionContext _context;
        private readonly Frame _frame;
        private readonly List<IConnectionAdapter> _connectionAdapters;
        private readonly TaskCompletionSource<object> _socketClosedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object> _frameInitializedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        private long _lastTimestamp;
        private long _timeoutTimestamp = long.MaxValue;
        private TimeoutAction _timeoutAction;

        private Task _lifetimeTask;

        public FrameConnection(FrameConnectionContext context)
        {
            _context = context;
            _frame = context.Frame;
            _connectionAdapters = context.ConnectionAdapters;
        }

        public string ConnectionId => _context.ConnectionId;
        public IPipeWriter Input => _context.Input.Writer;
        public IPipeReader Output => _context.Output.Reader;

        private PipeFactory PipeFactory => _context.PipeFactory;

        // Internal for testing
        internal PipeOptions AdaptedInputPipeOptions => new PipeOptions
        {
            ReaderScheduler = _context.ServiceContext.ThreadPool,
            WriterScheduler = InlineScheduler.Default,
            MaximumSizeHigh = _context.ServiceContext.ServerOptions.Limits.MaxRequestBufferSize ?? 0,
            MaximumSizeLow = _context.ServiceContext.ServerOptions.Limits.MaxRequestBufferSize ?? 0
        };

        internal PipeOptions AdaptedOutputPipeOptions => new PipeOptions
        {
            ReaderScheduler = InlineScheduler.Default,
            WriterScheduler = InlineScheduler.Default,
            MaximumSizeHigh = _context.ServiceContext.ServerOptions.Limits.MaxResponseBufferSize ?? 0,
            MaximumSizeLow = _context.ServiceContext.ServerOptions.Limits.MaxResponseBufferSize ?? 0
        };

        private IKestrelTrace Log => _context.ServiceContext.Log;

        public void StartRequestProcessing()
        {
            _lifetimeTask = ProcessRequestsAsync();
        }

        private async Task ProcessRequestsAsync()
        {
            try
            {
                var adaptedPipelineTask = Task.CompletedTask;
                var input = _context.Input.Reader;
                var output = _context.Output.Writer;

                // Set these before the first await, this is to make sure that we don't yield control
                // to the transport until we've added the connection to the connection manager
                _frame.TimeoutControl = this;
                _context.ServiceContext.ConnectionManager.AddConnection(_context.FrameConnectionId, this);
                _lastTimestamp = _context.ServiceContext.SystemClock.UtcNow.Ticks;

                if (_connectionAdapters.Count > 0)
                {
                    var adaptedPipeline = await ApplyConnectionAdaptersAsync();

                    if (adaptedPipeline == null)
                    {
                        // We need to assign these anyways because frame.Abort calls via OutputProducer.Abort
                        _frame.Input = input;
                        _frame.Output = new OutputProducer(output, _frame, ConnectionId, Log);

                        // Complete the input and output since the frame never started
                        input.Complete();
                        output.Complete();

                        _frameInitializedTcs.TrySetResult(null);

                        // We failed to initialize connection adapters
                        await _socketClosedTcs.Task;
                        return;
                    }

                    input = adaptedPipeline.Input.Reader;
                    output = adaptedPipeline.Output.Writer;

                    adaptedPipelineTask = adaptedPipeline.RunAsync();
                }

                _frame.Input = input;
                _frame.Output = new OutputProducer(output, _frame, ConnectionId, Log);
                _frameInitializedTcs.TrySetResult(null);

                await _frame.ProcessRequestsAsync();
                await adaptedPipelineTask;
                await _socketClosedTcs.Task;
            }
            catch (Exception ex)
            {
                // This would only *not* be set if something completely unexpected threw (like ConnectionManager.AddConnection)
                _frameInitializedTcs.TrySetResult(null);

                Log.LogError(0, ex, $"Unexpected exception in {nameof(FrameConnection)}.{nameof(ProcessRequestsAsync)}.");
            }
            finally
            {
                _context.ServiceContext.ConnectionManager.RemoveConnection(_context.FrameConnectionId);
                DisposeAdaptedConnections();
            }
        }

        public async void OnConnectionClosed(Exception ex)
        {
            await AbortAsyncInternal(ex);

            Log.ConnectionStop(ConnectionId);
            KestrelEventSource.Log.ConnectionStop(this);
            _socketClosedTcs.TrySetResult(null);
        }

        public async Task StopAsync()
        {
            await _frameInitializedTcs.Task;

            _frame.Stop();

            await _lifetimeTask;
        }

        public void Abort(Exception ex)
        {
            _ = AbortAsyncInternal(ex);
        }

        public async Task AbortAsync(Exception ex)
        {
            await AbortAsyncInternal(ex);

            await _lifetimeTask;
        }

        private async Task AbortAsyncInternal(Exception ex)
        {
            // Make sure the frame is fully initialized before aborting
            await _frameInitializedTcs.Task;

            // Abort the connection (if not already aborted)
            _frame.Abort(ex);
        }

        public void Timeout()
        {
            _frame.SetBadRequestState(RequestRejectionReason.RequestTimeout);
        }

        private async Task<AdaptedPipeline> ApplyConnectionAdaptersAsync()
        {
            var stream = new RawStream(_context.Input.Reader, _context.Output.Writer);
            var adapterContext = new ConnectionAdapterContext(stream);
            var adaptedConnections = new IAdaptedConnection[_connectionAdapters.Count];

            try
            {
                for (var i = 0; i < _connectionAdapters.Count; i++)
                {
                    var adaptedConnection = await _connectionAdapters[i].OnConnectionAsync(adapterContext);
                    adaptedConnections[i] = adaptedConnection;
                    adapterContext = new ConnectionAdapterContext(adaptedConnection.ConnectionStream);
                }
            }
            catch (Exception ex)
            {
                Log.LogError(0, ex, $"Uncaught exception from the {nameof(IConnectionAdapter.OnConnectionAsync)} method of an {nameof(IConnectionAdapter)}.");

                return null;
            }

            _frame.AdaptedConnections = adaptedConnections;

            return new AdaptedPipeline(adapterContext.ConnectionStream,
                                       _context.Input.Reader,
                                       _context.Output.Writer,
                                       PipeFactory.Create(AdaptedInputPipeOptions),
                                       PipeFactory.Create(AdaptedOutputPipeOptions),
                                       Log);
        }

        private void DisposeAdaptedConnections()
        {
            var adaptedConnections = _frame.AdaptedConnections;
            if (adaptedConnections != null)
            {
                for (int i = adaptedConnections.Length - 1; i >= 0; i--)
                {
                    adaptedConnections[i].Dispose();
                }
            }
        }

        public void Tick(DateTimeOffset now)
        {
            var timestamp = now.Ticks;

            // TODO: Use PlatformApis.VolatileRead equivalent again
            if (timestamp > Interlocked.Read(ref _timeoutTimestamp))
            {
                CancelTimeout();

                if (_timeoutAction == TimeoutAction.SendTimeoutResponse)
                {
                    Timeout();
                }

                _frame.Stop();
            }

            Interlocked.Exchange(ref _lastTimestamp, timestamp);
        }

        public void SetTimeout(long ticks, TimeoutAction timeoutAction)
        {
            Debug.Assert(_timeoutTimestamp == long.MaxValue, "Concurrent timeouts are not supported");

            AssignTimeout(ticks, timeoutAction);
        }

        public void ResetTimeout(long ticks, TimeoutAction timeoutAction)
        {
            AssignTimeout(ticks, timeoutAction);
        }

        public void CancelTimeout()
        {
            Interlocked.Exchange(ref _timeoutTimestamp, long.MaxValue);
        }

        private void AssignTimeout(long ticks, TimeoutAction timeoutAction)
        {
            _timeoutAction = timeoutAction;

            // Add Heartbeat.Interval since this can be called right before the next heartbeat.
            Interlocked.Exchange(ref _timeoutTimestamp, _lastTimestamp + ticks + Heartbeat.Interval.Ticks);
        }
    }
}
