﻿// Copyright (c) .NET Foundation. All rights reserved.
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
        private readonly ISystemClock _systemClock;
        private readonly TimeSpan _timeWithoutRequestsUntilIdle;
        private readonly TimeSpan _timerInterval;
        private readonly uint _timerTicksWithoutRequestsUntilIdle;

        private volatile string _dateValue;
        private bool _isDisposed = false;
        private bool _hadRequestsSinceLastTimerTick = false;
        private Timer _dateValueTimer;
        private object _timerLocker = new object();
        private int _timerTicksSinceLastRequest;

        /// <summary>
        /// Initializes a new instance of the <see cref="DateHeaderValueManager"/> class.
        /// </summary>
        public DateHeaderValueManager()
            : this(
                  systemClock: new SystemClock(),
                  timeWithoutRequestsUntilIdle: TimeSpan.FromSeconds(10),
                  timerInterval: TimeSpan.FromSeconds(1))
        {

        }

        // Internal for testing
        internal DateHeaderValueManager(
            ISystemClock systemClock,
            TimeSpan timeWithoutRequestsUntilIdle,
            TimeSpan timerInterval)
        {
            _systemClock = systemClock;
            _timeWithoutRequestsUntilIdle = timeWithoutRequestsUntilIdle;
            _timerInterval = timerInterval;

            // Calculate the number of timer ticks where no requests are seen before we're considered to be idle.
            // Once we're idle, the timer is shutdown to prevent code from running while there are no requests.
            // The timer is started again on the next request.
            _timerTicksWithoutRequestsUntilIdle = (uint)(_timeWithoutRequestsUntilIdle.TotalMilliseconds / _timerInterval.TotalMilliseconds);
        }

        /// <summary>
        /// Returns a value representing the current server date/time for use in the HTTP "Date" response header
        /// in accordance with http://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html#sec14.18
        /// </summary>
        /// <returns>The value.</returns>
        public string GetDateHeaderValue()
        {
            PumpTimer();

            // See https://msdn.microsoft.com/en-us/library/az4se3k1(v=vs.110).aspx#RFC1123 for info on the format
            // string used here.
            // The null-coalesce here is to protect against returning null after Dispose() is called, at which
            // point _dateValue will be null forever after.
            return _dateValue ?? _systemClock.UtcNow.ToString("r");
        }

        /// <summary>
        /// Releases all resources used by the current instance of <see cref="DateHeaderValueManager"/>.
        /// </summary>
        public void Dispose()
        {
            lock (_timerLocker)
            {
                if (_dateValueTimer != null)
                {
                    DisposeTimer();
                }

                _isDisposed = true;
            }
        }

        private void PumpTimer()
        {
            _hadRequestsSinceLastTimerTick = true;

            // If we're already disposed we don't care about starting the timer again. This avoids us having to worry
            // about requests in flight during dispose (not that that should actually happen) as those will just get
            // SystemClock.UtcNow (aka "the slow way").
            if (!_isDisposed && _dateValueTimer == null)
            {
                lock (_timerLocker)
                {
                    if (!_isDisposed && _dateValueTimer == null)
                    {
                        // Immediately assign the date value and start the timer again. We assign the value immediately
                        // here as the timer won't fire until the timer interval has passed and we want a value assigned
                        // inline now to serve requests that occur in the meantime.
                        _dateValue = _systemClock.UtcNow.ToString("r");
                        _dateValueTimer = new Timer(UpdateDateValue, state: null, dueTime: _timerInterval, period: _timerInterval);
                    }
                }
            }
        }

        // Called by the Timer (background) thread
        private void UpdateDateValue(object state)
        {
            // See http://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html#sec14.18 for required format of Date header
            _dateValue = _systemClock.UtcNow.ToString("r");

            if (_hadRequestsSinceLastTimerTick)
            {
                // We served requests since the last tick, reset the flag and return as we're still active
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
