// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public class Http1MessageBodyPipeReader : PipeReader, IAwaiter<ReadResult>
    {
        private readonly PipeReader _connectionReader;
        private readonly Http1MessageBody _messageBody;
        private readonly ITimeoutControl _timeoutControl;
        private readonly IKestrelTrace _log;
        private readonly Http1Connection _http1Connection;

        private ValueAwaiter<ReadResult> _lastAwaitedRead;
        private ReadResult _lastReadResult;

        private bool _startedReadTiming;
        private bool _readTimingEnabled;
        private bool _consumedAll;
        private long _lastUnconsumedLength;

        private bool _trimmedAll;
        private SequencePosition _trimConsumed;
        private SequencePosition _trimExamined;

        public Http1MessageBodyPipeReader(Http1Connection connection, Http1MessageBody body)
        {
            _connectionReader = connection.Input;
            _messageBody = body;
            _timeoutControl = connection.TimeoutControl;
            _log = connection.ServiceContext.Log;
            _http1Connection = connection;
            _readTimingEnabled = !body.RequestUpgrade;
        }

        public bool IsCompleted => _lastAwaitedRead.IsCompleted || _consumedAll;

        public void OnCompleted(Action continuation)
        {
            _lastAwaitedRead.OnCompleted(continuation);
        }

        public ReadResult GetResult()
        {
            if (_consumedAll)
            {
                return _lastReadResult;
            }

            // Let the ConnectionPipeReader throw before checking for a request timeouts.
            _lastReadResult = _lastAwaitedRead.GetResult();

            if (_timeoutControl.RequestTimedOut)
            {
                _connectionReader.AdvanceTo(_lastReadResult.Buffer.Start);
                BadHttpRequestException.Throw(RequestRejectionReason.RequestTimeout);
            }

            TrimLastReadResult();
            // Like in 2.0.0, we still measure the data rate for the *decoded* body.
            StopTimingRead();

            return _lastReadResult;
        }

        public override bool TryRead(out ReadResult result)
        {
            if (_consumedAll)
            {
                result = _lastReadResult;
                return true;
            }

            // TODO: Enforce min data rate for TryRead polling?
            if (_connectionReader.TryRead(out _lastReadResult))
            {
                TryInit(isCompleted: true);
                TrimLastReadResult();
                result = _lastReadResult;
                return true;
            }

            TryInit(isCompleted: false);
            result = default;
            return false;
        }

        public override ValueAwaiter<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (_consumedAll)
            {
                return new ValueAwaiter<ReadResult>(this);
            }

            if (_timeoutControl.RequestTimedOut)
            {
                // We only throw from GetResult(). It's important to manually cancel in case the
                // exception has already been observed and thrown.
                _connectionReader.CancelPendingRead();
            }

            _lastAwaitedRead = _connectionReader.ReadAsync(cancellationToken);
            TryInit(_lastAwaitedRead.IsCompleted);

            // Start timing mechanism even for "sync" reads to ensure all data is counted.
            StartTimingRead();

            return new ValueAwaiter<ReadResult>(this);
        }

        public override void AdvanceTo(SequencePosition consumed)
        {
            AdvanceTo(consumed, consumed);
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            // TODO: If we ever expose this PipeReader directly, we need to fix a deadlock
            // that can occur when a reader doesn't examine everything and waits for data
            // in the next chunk of a chunked request.
            if (_consumedAll)
            {
                return;
            }

            if (_trimmedAll)
            {
                _trimmedAll = false;
                _connectionReader.AdvanceTo(_trimConsumed, _trimExamined);
                return;
            }

            AdvanceMessageBody(consumed, examined);
            _connectionReader.AdvanceTo(consumed, examined);
        }

        public override void Complete(Exception exception = null)
        {
            if (exception != null)
            {
                _http1Connection.Abort(exception);
            }

            if (!_messageBody.RequestUpgrade)
            {
                _log.RequestBodyDone(_http1Connection.ConnectionIdFeature, _http1Connection.TraceIdentifier);
            }
        }

        public override void CancelPendingRead()
        {
            _connectionReader.CancelPendingRead();
        }

        public override void OnWriterCompleted(Action<Exception, object> callback, object state)
        {
            // REVIEW: We could fake this so it fires when the end of the body is retrieved, but should we do this now?
            throw new NotImplementedException();
        }

        private void TrimLastReadResult()
        {
            try
            {
                // The body pipe can experience an empty read when only encoding data is read from the connection pipe.
                _trimmedAll = !_messageBody.TryTrimReadResult(ref _lastReadResult, out _trimConsumed, out _trimExamined);
            }
            catch
            {
                _connectionReader.AdvanceTo(_lastReadResult.Buffer.Start);
                throw;
            }

            if (_trimmedAll)
            {
                // TODO: Call _connectionReader.ReadAsync() again so the reader of the body doesn't see empty reads in this case.
                _lastReadResult = new ReadResult(ReadOnlyBuffer<byte>.Empty, isCancelled: false, isCompleted: false);
            }
        }

        private void AdvanceMessageBody(in SequencePosition consumed, in SequencePosition examined)
        {
            long consumedBytes;

            // REVIEW: Is it possible for this condition to be false when the entire buffer was consumed?
            if (consumed == _lastReadResult.Buffer.End)
            {
                consumedBytes = _lastReadResult.Buffer.Length;
                _lastUnconsumedLength = 0;

                if (_lastReadResult.IsCompleted)
                {
                    _consumedAll = true;
                    _lastReadResult = new ReadResult(ReadOnlyBuffer<byte>.Empty, isCancelled: false, isCompleted: true);
                }
            }
            else
            {
                consumedBytes = _lastReadResult.Buffer.Slice(_lastReadResult.Buffer.Start, consumed).Length;
                _lastUnconsumedLength = _lastReadResult.Buffer.Length - consumedBytes;
            }

            _messageBody.Advance(consumedBytes);
        }

        private void TryInit(bool isCompleted)
        {
            if (_http1Connection.HasStartedConsumingRequestBody)
            {
                return;
            }

            _messageBody.OnReadStarting();
            _http1Connection.HasStartedConsumingRequestBody = true;

            if (!_messageBody.RequestUpgrade)
            {
                _log.RequestBodyStart(_http1Connection.ConnectionIdFeature, _http1Connection.TraceIdentifier);
            }

            if (!isCompleted)
            {
                _messageBody.TryProduceContinue();
            }
        }

        private void StartTimingRead()
        {
            if (!_readTimingEnabled)
            {
                return;
            }

            if (!_startedReadTiming)
            {
                _startedReadTiming = true;
                _timeoutControl.StartTimingReads();
            }
            else
            {
                _timeoutControl.ResumeTimingReads();
            }
        }

        private void StopTimingRead()
        {
            if (!_readTimingEnabled || !_startedReadTiming)
            {
                return;
            }

            // Don't double count bytes that weren't consumed in the last read.
            _timeoutControl.BytesRead(_lastReadResult.Buffer.Length - _lastUnconsumedLength);

            if (_lastReadResult.IsCompleted)
            {
                _readTimingEnabled = false;
                _timeoutControl.StopTimingReads();
            }
            else
            {
                _timeoutControl.PauseTimingReads();
            }
        }
    }
}
