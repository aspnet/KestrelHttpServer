// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    /// <summary>
    /// Manages the generation of the date header value.
    /// </summary>
    public class DateHeaderValueManager : ITick, IDisposable
    {
        private static readonly byte[] _datePreambleBytes = Encoding.ASCII.GetBytes("\r\nDate: ");

        private readonly ISystemClock _systemClock;
        private readonly TimeSpan _timeWithoutRequestsUntilIdle;

        private DateHeaderValues _dateValues;

        private volatile bool _isDisposed = false;
        private volatile bool _hadRequestsSinceLastTimerTick = false;
        private long _lastRequestSeenTicks;
        private volatile bool _timerIsRunning;

        /// <summary>
        /// Initializes a new instance of the <see cref="DateHeaderValueManager"/> class.
        /// </summary>
        public DateHeaderValueManager()
            : this(systemClock: new SystemClock())
        {
        }

        // Internal for testing
        internal DateHeaderValueManager(ISystemClock systemClock)
            : this(systemClock: systemClock, timeWithoutRequestsUntilIdle: TimeSpan.FromSeconds(10))
        {
        }

        // Internal for testing
        internal DateHeaderValueManager(
            ISystemClock systemClock,
            TimeSpan timeWithoutRequestsUntilIdle)
        {
            if (systemClock == null)
            {
                throw new ArgumentNullException(nameof(systemClock));
            }

            _systemClock = systemClock;
            _timeWithoutRequestsUntilIdle = timeWithoutRequestsUntilIdle;
        }

        /// <summary>
        /// Returns a value representing the current server date/time for use in the HTTP "Date" response header
        /// in accordance with http://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html#sec14.18
        /// </summary>
        /// <returns>The value in string and byte[] format.</returns>
        public DateHeaderValues GetDateHeaderValues()
        {
            _hadRequestsSinceLastTimerTick = !_isDisposed;

            if (!_timerIsRunning)
            {
                StartTimer();
            }

            return _dateValues;
        }

        /// <summary>
        /// Releases all resources used by the current instance of <see cref="DateHeaderValueManager"/>.
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _hadRequestsSinceLastTimerTick = false;
                _timerIsRunning = false;
            }
        }

        /// <summary>
        /// Starts the timer
        /// </summary>
        private void StartTimer()
        {
            SetDateValues(_systemClock.UtcNow);
            _timerIsRunning = !_isDisposed;
        }

        // Called by the Timer (background) thread
        public void Tick(DateTimeOffset now)
        {
            if (_timerIsRunning)
            {
                SetDateValues(now);

                if (_hadRequestsSinceLastTimerTick)
                {
                    // We served requests since the last tick, reset the flag and return as we're still active
                    _hadRequestsSinceLastTimerTick = false;
                    Interlocked.Exchange(ref _lastRequestSeenTicks, now.Ticks);
                    return;
                }

                // No requests since the last timer tick, we need to check if we're beyond the idle threshold
                // TODO: Use PlatformApis.VolatileRead equivalent again
                if ((now.Ticks - Interlocked.Read(ref _lastRequestSeenTicks)) >= _timeWithoutRequestsUntilIdle.Ticks)
                {
                    // No requests since idle threshold so stop generating new date values.
                    _timerIsRunning = false;
                }
            }
        }

        /// <summary>
        /// Sets date values from a provided ticks value
        /// </summary>
        /// <param name="value">A DateTimeOffset value</param>
        private void SetDateValues(DateTimeOffset value)
        {
            var dateValue = HeaderUtilities.FormatDate(value);
            var dateBytes = new byte[_datePreambleBytes.Length + dateValue.Length];
            Buffer.BlockCopy(_datePreambleBytes, 0, dateBytes, 0, _datePreambleBytes.Length);
            Encoding.ASCII.GetBytes(dateValue, 0, dateValue.Length, dateBytes, _datePreambleBytes.Length);

            var dateValues = new DateHeaderValues()
            {
                Bytes = dateBytes,
                String = dateValue
            };
            Volatile.Write(ref _dateValues, dateValues);
        }

        public class DateHeaderValues
        {
            public byte[] Bytes;
            public string String;
        }
    }
}
