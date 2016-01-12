// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    public class FrameState : IDisposable
    {
        private static readonly TimerCallback _timeoutRequest = (o) => ((FrameState)o).TimeoutRequest();

        private readonly Frame _frame;
        private readonly IKestrelServerInformation _settings;
        private readonly Timer _timeout;
        // enum doesn't work with Interlocked
        private int _frameState;


        public int CurrentState => Volatile.Read(ref _frameState);

        public FrameState(Frame frame, IKestrelServerInformation settings)
        {
            _frame = frame;
            _settings = settings;
            _timeout = new Timer(_timeoutRequest, this, Timeout.Infinite, Timeout.Infinite);
            _frameState = RequestState.NotStarted;
        }

        private void TimeoutRequest()
        {
            // Don't abort if debugging
            if (!Debugger.IsAttached && TransitionToState(RequestState.Timeout) == RequestState.Timeout)
            {
                _frame.Abort();
            }
        }

        public int TransitionToState(int state)
        {
            int prevState = Volatile.Read(ref _frameState);

            switch (state)
            {
                case RequestState.Waiting:
                    return TransitionToWaiting(prevState);
                case RequestState.ReadingHeaders:
                    if (prevState == RequestState.ReadingHeaders) return RequestState.ReadingHeaders;
                    // can only transition to ReadingHeaders from Waiting
                    prevState = Interlocked.CompareExchange(ref _frameState, RequestState.ReadingHeaders, RequestState.Waiting);
                    if (prevState == RequestState.Waiting)
                    {
                        // only reset timer on transition into this state
                        _timeout.Change((int)_settings.HeadersCompleteTimeout.TotalMilliseconds, Timeout.Infinite);
                        return RequestState.ReadingHeaders;
                    }
                    break;
                case RequestState.ExecutingRequest:
                    // can only transition to ExecutingRequest from ReadingHeaders
                    prevState = Interlocked.CompareExchange(ref _frameState, RequestState.ExecutingRequest, RequestState.ReadingHeaders);
                    if (prevState == RequestState.ReadingHeaders)
                    {
                        // only reset timer if state correct
                        _timeout.Change((int)_settings.ExecutionTimeout.TotalMilliseconds, Timeout.Infinite);
                        return RequestState.ExecutingRequest;
                    }
                    break;
                case RequestState.UpgradedRequest:
                    // can only transition to UpgradedRequest from ExecutingRequest
                    prevState = Interlocked.CompareExchange(ref _frameState, RequestState.UpgradedRequest, RequestState.ExecutingRequest);
                    if (prevState == RequestState.ExecutingRequest)
                    {
                        // switch off timer for upgraded request; upgraded pipeline should handle its own timeouts
                        _timeout.Change(Timeout.Infinite, Timeout.Infinite);
                        return RequestState.UpgradedRequest;
                    }
                    break;
                case RequestState.Stopping:
                    // marker state, can't transition into it.
                    throw new InvalidOperationException();
                case RequestState.Timeout:
                    if (prevState >= RequestState.Timeout) return prevState;
                    // can transition to Timeout from states below it
                    do
                    {
                        prevState = Interlocked.CompareExchange(ref _frameState, RequestState.Timeout, prevState);
                    } while (prevState < RequestState.Timeout);
                    return prevState > RequestState.Timeout ? prevState : RequestState.Timeout;
                case RequestState.Stopped:
                    if (prevState >= RequestState.Stopped) return prevState;
                    // can transition to Stopped from states below it
                    do
                    {
                        prevState = Interlocked.CompareExchange(ref _frameState, RequestState.Stopped, prevState);
                    } while (prevState < RequestState.Stopped);
                    return prevState > RequestState.Stopped ? prevState : RequestState.Stopped;
                case RequestState.Aborted:
                    // can transition to Aborted from any state
                    _frameState = RequestState.Aborted;
                    return prevState; // return previous state to say if already aborted
            }
            return prevState;
        }

        public int TransitionToWaiting(int prevState)
        {
            switch (prevState)
            {
                case RequestState.ExecutingRequest:
                    prevState = Interlocked.CompareExchange(ref _frameState, RequestState.Waiting, RequestState.ExecutingRequest);
                    if (prevState == RequestState.ExecutingRequest)
                    {
                        // only reset timer on transition into this state
                        _timeout.Change((int)_settings.KeepAliveTimeout.TotalMilliseconds, Timeout.Infinite);
                        return RequestState.Waiting;
                    }
                    break;
                case RequestState.UpgradedRequest:
                    prevState = Interlocked.CompareExchange(ref _frameState, RequestState.Waiting, RequestState.UpgradedRequest);
                    if (prevState == RequestState.UpgradedRequest)
                    {
                        // only reset timer on transition into this state
                        _timeout.Change((int)_settings.KeepAliveTimeout.TotalMilliseconds, Timeout.Infinite);
                        return RequestState.Waiting;
                    }
                    break;
                case RequestState.NotStarted:
                    prevState = Interlocked.CompareExchange(ref _frameState, RequestState.Waiting, RequestState.NotStarted);
                    if (prevState == RequestState.NotStarted)
                    {
                        // only reset timer on transition into this state
                        _timeout.Change((int)_settings.KeepAliveTimeout.TotalMilliseconds, Timeout.Infinite);
                        return RequestState.Waiting;
                    }
                    break;
            }
            return prevState;
        }

        public void Dispose()
        {
            _timeout.Dispose();
        }
    }
}
