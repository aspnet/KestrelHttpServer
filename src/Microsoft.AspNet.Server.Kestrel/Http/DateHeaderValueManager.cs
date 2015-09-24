// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    /// <summary>
    /// Manages the generation of the date header value.
    /// </summary>
    public class DateHeaderValueManager : IDisposable
    {
        private readonly TimeSpan _timeWithoutRequestsUntilIdle = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _timerInterval = TimeSpan.FromSeconds(1);
        private readonly uint _timerTicksWithoutRequestsUntilIdle;
        private readonly ISystemClock _systemClock;

        private volatile string _dateValue;
        private bool _isDisposed = false;
        private bool _hadRequestsSinceLastTimerTick = false;
        private Timer _dateValueTimer;
        private object _timerLocker = new object();
        private int _timerTicksSinceLastRequest;

        public DateHeaderValueManager()
            : this(new SystemClock())
        {

        }

        public DateHeaderValueManager(ISystemClock systemClock)
        {
            _systemClock = systemClock;
            _timerTicksWithoutRequestsUntilIdle = (uint)(_timeWithoutRequestsUntilIdle.TotalMilliseconds / _timerInterval.TotalMilliseconds);
        }

        public string GetDateHeaderValue()
        {
            PumpTimer();

            // The null-coalesce here is to protect against this getting called after Dispose() is called, at which point
            // _dateValue will be null forever more.
            return _dateValue ?? DateTime.UtcNow.ToString("r");
        }
        
        public void Dispose()
        {
            lock (_timerLocker)
            {
                if (_dateValueTimer != null)
                {
                    DisposeTimer();
                    _isDisposed = true;
                }
            }
        }

        private void PumpTimer()
        {
            _hadRequestsSinceLastTimerTick = true;

            // If we're already disposed we don't care about starting the timer again. This avoids us having to worry
            // about requests in flight during dispose (not that that should actually happen) as those will just get
            // DateTime.UtcNow (aka "the slow way").
            if (!_isDisposed && _dateValueTimer == null)
            {
                lock (_timerLocker)
                {
                    if (!_isDisposed && _dateValueTimer == null)
                    {
                        // Immediately assign the date value and start the timer again
                        _dateValue = DateTime.UtcNow.ToString("r");
                        _dateValueTimer = new Timer(UpdateDateValue, state: null, dueTime: _timerInterval, period: _timerInterval);
                    }
                }
            }
        }

        // Called by the Timer (background) thread
        private void UpdateDateValue(object state)
        {
            // See http://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html#sec14.18 for required format of Date header
            _dateValue = DateTime.UtcNow.ToString("r");

            if (_hadRequestsSinceLastTimerTick)
            {
                // We served requests since the last tick, just return as we're still active
                _hadRequestsSinceLastTimerTick = false;
                _timerTicksSinceLastRequest = 0;
                return;
            }

            // No requests since the last timer tick, we need to check if we're beyond the idle threshold
            _timerTicksSinceLastRequest++;
            if (_timerTicksSinceLastRequest == _timerTicksWithoutRequestsUntilIdle)
            {
                // No requests since idle threshold so stop the timer if it's still running
                if (_dateValueTimer != null)
                {
                    lock (_timerLocker)
                    {
                        if (_dateValueTimer != null)
                        {
                            DisposeTimer();
                        }
                    }
                }
            }
        }

        private void DisposeTimer()
        {
            _dateValueTimer.Dispose();
            _dateValueTimer = null;
            _dateValue = null;
        }
    }
}
