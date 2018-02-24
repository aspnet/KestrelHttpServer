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

        private ValueAwaiter<ReadResult> _lastAwaitedRead;
        private ReadResult _lastReadResult;
        private Exception _encodingException;
        private bool _consumedAll;
        private bool _examinedAll;

        private bool _trimmedAll;
        private SequencePosition _trimConsumed;
        private SequencePosition _trimExamined;

        public Http1MessageBodyPipeReader(PipeReader connectionReader, Http1MessageBody messageBody)
        {
            _connectionReader = connectionReader;
            _messageBody = messageBody;
        }

        public bool IsCompleted => _examinedAll || _lastAwaitedRead.IsCompleted;

        public override void AdvanceTo(SequencePosition consumed)
        {
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

            AdvanceMessageBody(consumed);
            _connectionReader.AdvanceTo(consumed);
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

            AdvanceMessageBody(consumed);
            _connectionReader.AdvanceTo(consumed, examined);
        }

        public override void CancelPendingRead()
        {
            _connectionReader.CancelPendingRead();
        }

        public override void Complete(Exception exception = null)
        {
            // REVIEW: Should we log the exception here?
        }

        public ReadResult GetResult()
        {
            if (_encodingException != null)
            {
                throw _encodingException;
            }

            if (!_consumedAll)
            {
                _lastReadResult = _lastAwaitedRead.GetResult();
                TrimLastReadResult();
            }

            return _lastReadResult;
        }

        public void OnCompleted(Action continuation)
        {
            _lastAwaitedRead.OnCompleted(continuation);
        }

        // REVIEW: We could fake this so it fires when the end of the body is retrieved, but should we?
        public override void OnWriterCompleted(Action<Exception, object> callback, object state)
        {
            throw new NotImplementedException();
        }

        public override ValueAwaiter<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (!_consumedAll)
            {
                _lastAwaitedRead = _connectionReader.ReadAsync(cancellationToken);
            }

            if (!_lastAwaitedRead.IsCompleted)
            {
                _messageBody.TryProduceContinue();
            }

            return new ValueAwaiter<ReadResult>(this);
        }

        public override bool TryRead(out ReadResult result)
        {
            if (_encodingException != null)
            {
                throw _encodingException;
            }

            if (_consumedAll)
            {
                result = _lastReadResult;
                return true;
            }

            if (_connectionReader.TryRead(out _lastReadResult))
            {
                TrimLastReadResult();
                result = _lastReadResult;
                return true;
            }

            result = default(ReadResult);
            return false;
        }

        private void AdvanceMessageBody(in SequencePosition consumed)
        {
            long consumedBytes;

            // REVIEW: Is it possible for this condition to be false when the entire buffer was consumed?
            if (consumed == _lastReadResult.Buffer.End)
            {
                consumedBytes = _lastReadResult.Buffer.Length;

                if (_examinedAll)
                {
                    _lastReadResult = new ReadResult(ReadOnlyBuffer<byte>.Empty, isCancelled: false, isCompleted: true);
                    _consumedAll = true;
                }
            }
            else
            {
                consumedBytes = _lastReadResult.Buffer.Slice(_lastReadResult.Buffer.Start, consumed).Length;
            }

            _messageBody.Advance(consumedBytes);
        }

        private void TrimLastReadResult()
        {
            // The body pipe experiences an empty read when only encoding data is read from the connection pipe.
            try
            {
                _trimmedAll = !_messageBody.TryTrimReadResult(ref _lastReadResult, out _trimConsumed, out _trimExamined);
            }
            catch (Exception ex)
            {
                _encodingException = ex;
                _connectionReader.AdvanceTo(_lastReadResult.Buffer.Start);
                throw;
            }

            if (_trimmedAll)
            {
                _lastReadResult = new ReadResult(ReadOnlyBuffer<byte>.Empty, isCancelled: false, isCompleted: false);
            }

            _examinedAll = _lastReadResult.IsCompleted;
        }
    }
}
