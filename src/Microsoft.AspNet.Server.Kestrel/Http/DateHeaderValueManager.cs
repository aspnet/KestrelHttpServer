// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class DateHeaderValueManager : IDisposable
    {
        private readonly TimeSpan _timeWithoutRequestsUntilIdle = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _timerInterval = TimeSpan.FromSeconds(1);
        private readonly uint _timerTicksWithoutRequestsUntilIdle;

        private volatile string _dateValue;
        private bool _isDisposed = false;
        private bool _hadRequestsSinceLastTimerTick = false;
        private Timer _dateValueTimer;
        private object _timerLocker = new object();
        private int _timerTicksSinceLastRequest;

        public DateHeaderValueManager()
        {
            _timerTicksWithoutRequestsUntilIdle = (uint)(_timeWithoutRequestsUntilIdle.TotalMilliseconds / _timerInterval.TotalMilliseconds);
        }

        public string GetDateHeaderValue()
        {
            PumpTimer();

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

        private void UpdateDateValue(object state)
        {
            _dateValue = DateTime.UtcNow.ToString("r");

            if (_hadRequestsSinceLastTimerTick)
            {
                // We served requests since the last tick, just return
                _hadRequestsSinceLastTimerTick = false;
                _timerTicksSinceLastRequest = 0;
                return;
            }

            // No requests since the last timer tick
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
