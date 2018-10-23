// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public class DataRateRecorder
    {
        private const long _bucketIntervalTicks = 5 * TimeSpan.TicksPerSecond;
        private const int _numBuckets = 12;

        private readonly MinDataRate _minRate;

        private long _lastBucketStartTimestamp;
        private int _concurrentIOAwaiters;
        private bool _startedAwaitingIoThisBeat;

        // Initialize to _numBuckets instead of 0, so the index used in CheckForTimeout is always positive.
        private int _currentBucketIndex = _numBuckets;
        private Bucket[] _buckets = new Bucket[_numBuckets];

        public DataRateRecorder(MinDataRate minRate, DateTimeOffset now)
        {
            _minRate = minRate;
            _lastBucketStartTimestamp = now.Ticks;
        }

        public bool CheckForTimeout(DateTimeOffset now)
        {
            var bucket = _buckets[_currentBucketIndex];

            if (_startedAwaitingIoThisBeat || _concurrentIOAwaiters > 0)
            {
                // Even if the time between beats is larger than the heartbeat interval, we don't want to penalize
                // the connection for this, so always increment TicksAwaitingIO by the heartbeat interval.
                bucket.TicksAwaitingIO += Heartbeat.Interval.Ticks;
                _startedAwaitingIoThisBeat = false;
            }

            _buckets[_currentBucketIndex].TicksInBucket++;

            while (_lastBucketStartTimestamp > now.Ticks)
            {
                _lastBucketStartTimestamp += _bucketIntervalTicks;
                _currentBucketIndex = _currentBucketIndex + 1 % _numBuckets;
                bucket = _buckets[_currentBucketIndex];
                bucket.Clear();
            }

            var totalTicksAwaitingIO = 0L;
            var totalBytesTransferred = 0L;

            for (var i = 0; i < _numBuckets; i++)
            {
                bucket = _buckets[_currentBucketIndex - i];

                totalTicksAwaitingIO += bucket.TicksAwaitingIO;
                totalBytesTransferred += Interlocked.Read(ref bucket.BytesTransferred);

                if (totalTicksAwaitingIO > _minRate.GracePeriod.Ticks)
                {
                    var secondsAwaitingIO = (double)totalTicksAwaitingIO / TimeSpan.TicksPerSecond;
                    var rate = totalBytesTransferred / secondsAwaitingIO;

                    // If the average rate is above the min rate for any contiguous span of buckets
                    // ending in the most recent bucket, don't fire a timeout.
                    if (rate >= _minRate.BytesPerSecond)
                    {
                        return false;
                    }
                }
            }

            // The recorded rate was not above the min rate for any of the checked spans of buckets. If at least the
            // grace period's worth of time has been spent awaiting IO, fire a timeout.
            return totalTicksAwaitingIO > _minRate.GracePeriod.Ticks;
        }

        public void BytesTransferred(long count)
        {
            Interlocked.Add(ref _buckets[_currentBucketIndex].BytesTransferred, count);
        }

        public void StartAwaitingIO()
        {
            Interlocked.Increment(ref _concurrentIOAwaiters);
            _startedAwaitingIoThisBeat = true;
        }

        public void StopAwaitingIO()
        {
            Interlocked.Decrement(ref _concurrentIOAwaiters);
        }

        private struct Bucket
        {
            public long BytesTransferred;
            public long TicksAwaitingIO;
            public long TicksInBucket;

            public void Clear()
            {
                BytesTransferred = 0;
                TicksAwaitingIO = 0;
                TicksInBucket = 0;
            }
        }
    }
}
