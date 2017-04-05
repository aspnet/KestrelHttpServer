// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public class Heartbeat : IDisposable
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1000);

        private readonly IEnumerable<ITick> _callbacks;
        private readonly TimeSpan _interval;
        private readonly ISystemClock _systemClock;
        private readonly IKestrelTrace _trace;
        private readonly Timer _timer;
        private int _executingOnBeat;

        public Heartbeat(IEnumerable<ITick> callbacks, ISystemClock systemClock, IKestrelTrace trace)
            : this(callbacks, systemClock, Interval, trace)
        {
        }

        // For testing
        internal Heartbeat(IEnumerable<ITick> callbacks, ISystemClock systemClock, TimeSpan interval, IKestrelTrace trace)
        {
            _callbacks = callbacks;
            _interval = interval;
            _systemClock = systemClock;
            _trace = trace;
            _timer = new Timer(OnBeat, state: this, dueTime: TimeSpan.Zero, period: _interval);
        }
        
        // Called by the Timer (background) thread
        private void OnBeat(object state)
        {
            var now = _systemClock.UtcNow;

            if (Interlocked.Exchange(ref _executingOnBeat, 1) == 0)
            {
                try
                {
                    foreach (var callback in _callbacks)
                    {
                        callback.Tick(now);
                    }
                }
                catch (Exception ex)
                {
                    _trace.LogError($"{nameof(Heartbeat)}.{nameof(OnBeat)}", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _executingOnBeat, 0);
                }
            }
            else
            {
                _trace.TimerSlow(_interval, now);
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
