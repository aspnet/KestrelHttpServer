// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public class FrameConnection : ITimeoutControl, IPipe
    {
        private readonly FrameConnectionContext _context;
        private readonly TaskCompletionSource<object> _socketClosedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        private Frame _frame;

        private long _lastTimestamp;
        private long _timeoutTimestamp = long.MaxValue;
        private TimeoutAction _timeoutAction;

        private object _readTimingLock = new object();
        private bool _readTimingEnabled;
        private bool _readTimingPauseRequested;
        private long _readTimingElapsedTicks;
        private long _readTimingBytesRead;

        private object _writeTimingLock = new object();
        private int _writeTimingWrites;
        private long _writeTimingTimeoutTimestamp;

        private Task _lifetimeTask;

        public FrameConnection(FrameConnectionContext context)
        {
            _context = context;
        }

        // For testing
        internal Frame Frame => _frame;
        internal IDebugger Debugger { get; set; } = DebuggerWrapper.Singleton;

        public bool TimedOut { get; private set; }

        public string ConnectionId => _context.ConnectionId;
        public IPipeWriter Input => _context.Input.Writer;
        public IPipeReader Output => _context.Output.Reader;

        private PipeFactory PipeFactory => _context.ConnectionInformation.PipeFactory;

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

        public IPipeReader Reader => _context.Output.Reader;

        public IPipeWriter Writer => _context.Output.Writer;

        public Task StartRequestProcessing<TContext>(IHttpApplication<TContext> application)
        {
            _lifetimeTask = ProcessRequestsAsync(application);
            return _lifetimeTask;
        }

        private async Task ProcessRequestsAsync<TContext>(IHttpApplication<TContext> application)
        {
            using (BeginConnectionScope())
            {
                try
                {
                    Log.ConnectionStart(ConnectionId);
                    KestrelEventSource.Log.ConnectionStart(this, _context.ConnectionInformation);

                    var input = _context.Input.Reader;
                    var output = _context.Output;

                    // _frame must be initialized before adding the connection to the connection manager
                    CreateFrame(application, input, output);

                    // Do this before the first await so we don't yield control to the transport until we've
                    // added the connection to the connection manager
                    _context.ServiceContext.ConnectionManager.AddConnection(_context.FrameConnectionId, this);
                    _lastTimestamp = _context.ServiceContext.SystemClock.UtcNow.Ticks;

                    await _frame.ProcessRequestsAsync();
                    await _socketClosedTcs.Task;
                }
                catch (Exception ex)
                {
                    Log.LogError(0, ex, $"Unexpected exception in {nameof(FrameConnection)}.{nameof(ProcessRequestsAsync)}.");
                }
                finally
                {
                    _context.ServiceContext.ConnectionManager.RemoveConnection(_context.FrameConnectionId);

                    if (_frame.WasUpgraded)
                    {
                        _context.ServiceContext.ConnectionManager.UpgradedConnectionCount.ReleaseOne();
                    }
                    else
                    {
                        _context.ServiceContext.ConnectionManager.NormalConnectionCount.ReleaseOne();
                    }

                    Log.ConnectionStop(ConnectionId);
                    KestrelEventSource.Log.ConnectionStop(this);
                }
            }
        }

        internal void CreateFrame<TContext>(IHttpApplication<TContext> application, IPipeReader input, IPipe output)
        {
            _frame = new Frame<TContext>(application, new FrameContext
            {
                ConnectionId = _context.ConnectionId,
                ConnectionInformation = _context.ConnectionInformation,
                ServiceContext = _context.ServiceContext,
                TimeoutControl = this,
                Input = input,
                Output = output
            });
        }

        public void OnConnectionClosed(Exception ex)
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            // Abort the connection (if not already aborted)
            _frame.Abort(ex);

            _socketClosedTcs.TrySetResult(null);
        }

        public Task StopAsync()
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            _frame.Stop();

            return _lifetimeTask;
        }

        public void Abort(Exception ex)
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            // Abort the connection (if not already aborted)
            _frame.Abort(ex);
        }

        public Task AbortAsync(Exception ex)
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            // Abort the connection (if not already aborted)
            _frame.Abort(ex);

            return _lifetimeTask;
        }

        public void SetTimeoutResponse()
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            _frame.SetBadRequestState(RequestRejectionReason.RequestTimeout);
        }

        public void Timeout()
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            TimedOut = true;
            _frame.Stop();
        }

        public void Tick(DateTimeOffset now)
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            var timestamp = now.Ticks;

            CheckForTimeout(timestamp);
            CheckForReadDataRateTimeout(timestamp);
            CheckForWriteDataRateTimeout(timestamp);

            Interlocked.Exchange(ref _lastTimestamp, timestamp);
        }

        private void CheckForTimeout(long timestamp)
        {
            if (TimedOut)
            {
                return;
            }

            // TODO: Use PlatformApis.VolatileRead equivalent again
            if (timestamp > Interlocked.Read(ref _timeoutTimestamp))
            {
                if (!Debugger.IsAttached)
                {
                    CancelTimeout();

                    if (_timeoutAction == TimeoutAction.SendTimeoutResponse)
                    {
                        SetTimeoutResponse();
                    }

                    Timeout();
                }
            }
        }

        private void CheckForReadDataRateTimeout(long timestamp)
        {
            // The only time when both a timeout is set and the read data rate could be enforced is
            // when draining the request body. Since there's already a (short) timeout set for draining,
            // it's safe to not check the data rate at this point.
            if (TimedOut || Interlocked.Read(ref _timeoutTimestamp) != long.MaxValue)
            {
                return;
            }

            lock (_readTimingLock)
            {
                if (_readTimingEnabled)
                {
                    // Reference in local var to avoid torn reads in case the min rate is changed via IHttpMinRequestBodyDataRateFeature
                    var minRequestBodyDataRate = _frame.MinRequestBodyDataRate;

                    _readTimingElapsedTicks += timestamp - _lastTimestamp;

                    if (minRequestBodyDataRate?.BytesPerSecond > 0 && _readTimingElapsedTicks > minRequestBodyDataRate.GracePeriod.Ticks)
                    {
                        var elapsedSeconds = (double)_readTimingElapsedTicks / TimeSpan.TicksPerSecond;
                        var rate = Interlocked.Read(ref _readTimingBytesRead) / elapsedSeconds;

                        if (rate < minRequestBodyDataRate.BytesPerSecond && !Debugger.IsAttached)
                        {
                            Log.RequestBodyMininumDataRateNotSatisfied(_context.ConnectionId, _frame.TraceIdentifier, minRequestBodyDataRate.BytesPerSecond);
                            Timeout();
                        }
                    }

                    // PauseTimingReads() cannot just set _timingReads to false. It needs to go through at least one tick
                    // before pausing, otherwise _readTimingElapsed might never be updated if PauseTimingReads() is always
                    // called before the next tick.
                    if (_readTimingPauseRequested)
                    {
                        _readTimingEnabled = false;
                        _readTimingPauseRequested = false;
                    }
                }
            }
        }

        private void CheckForWriteDataRateTimeout(long timestamp)
        {
            if (TimedOut)
            {
                return;
            }

            lock (_writeTimingLock)
            {
                if (_writeTimingWrites > 0 && timestamp > _writeTimingTimeoutTimestamp && !Debugger.IsAttached)
                {
                    TimedOut = true;
                    Log.ResponseMininumDataRateNotSatisfied(_frame.ConnectionIdFeature, _frame.TraceIdentifier);
                    Abort(new TimeoutException());
                }
            }
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

        public void StartTimingReads()
        {
            lock (_readTimingLock)
            {
                _readTimingElapsedTicks = 0;
                _readTimingBytesRead = 0;
                _readTimingEnabled = true;
            }
        }

        public void StopTimingReads()
        {
            lock (_readTimingLock)
            {
                _readTimingEnabled = false;
            }
        }

        public void PauseTimingReads()
        {
            lock (_readTimingLock)
            {
                _readTimingPauseRequested = true;
            }
        }

        public void ResumeTimingReads()
        {
            lock (_readTimingLock)
            {
                _readTimingEnabled = true;

                // In case pause and resume were both called between ticks
                _readTimingPauseRequested = false;
            }
        }

        public void BytesRead(long count)
        {
            Interlocked.Add(ref _readTimingBytesRead, count);
        }

        public void StartTimingWrite(long size)
        {
            lock (_writeTimingLock)
            {
                var minResponseDataRate = _frame.MinResponseDataRate;

                if (minResponseDataRate != null)
                {
                    var timeoutTicks = Math.Max(
                        minResponseDataRate.GracePeriod.Ticks,
                        TimeSpan.FromSeconds(size / minResponseDataRate.BytesPerSecond).Ticks);

                    if (_writeTimingWrites == 0)
                    {
                        // Add Heartbeat.Interval since this can be called right before the next heartbeat.
                        _writeTimingTimeoutTimestamp = _lastTimestamp + Heartbeat.Interval.Ticks;
                    }

                    _writeTimingTimeoutTimestamp += timeoutTicks;
                    _writeTimingWrites++;
                }
            }
        }

        public void StopTimingWrite()
        {
            lock (_writeTimingLock)
            {
                _writeTimingWrites--;
            }
        }

        private IDisposable BeginConnectionScope()
        {
            if (Log.IsEnabled(LogLevel.Critical))
            {
                return Log.BeginScope(new ConnectionLogScope(ConnectionId));
            }

            return null;
        }

        public void Reset()
        {
        }
    }
}
