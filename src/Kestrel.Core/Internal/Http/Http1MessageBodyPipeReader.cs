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
        private bool _examinedAll;
        private bool _consumedAll;

        public Http1MessageBodyPipeReader(PipeReader connectionReader, Http1MessageBody messageBody)
        {
            _connectionReader = connectionReader;
            _messageBody = messageBody;
        }

        public bool IsCompleted => _consumedAll || _lastAwaitedRead.IsCompleted;

        public override void AdvanceTo(SequencePosition consumed)
        {
            if (_consumedAll)
            {
                return;
            }

            AdvanceMessageBody(consumed);
            _connectionReader.AdvanceTo(consumed);
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            if (_consumedAll)
            {
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
            if (exception != null)
            {
                _connectionReader.Complete(exception);
            }
        }

        public ReadResult GetResult()
        {
            if (_consumedAll)
            {
                _lastReadResult = new ReadResult(ReadOnlyBuffer<byte>.Empty, isCancelled: false, isCompleted: true);
            }
            else
            {
                SetLastReadResult(_lastAwaitedRead.GetResult());
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
            _lastAwaitedRead = _connectionReader.ReadAsync(cancellationToken);
            return new ValueAwaiter<ReadResult>(this);
        }

        public override bool TryRead(out ReadResult result)
        {
            if (_connectionReader.TryRead(out result))
            {
                SetLastReadResult(result);
                return true;
            }

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
                    _consumedAll = true;
                }
            }
            else
            {
                consumedBytes = _lastReadResult.Buffer.Slice(_lastReadResult.Buffer.Start, consumed).Length;
            }

            _messageBody.Advance(consumedBytes);
        }

        private void SetLastReadResult(ReadResult readResult)
        {
            _lastReadResult = readResult;
            _messageBody.TrimReadResult(ref _lastReadResult);
            _examinedAll = _lastReadResult.IsCompleted;
        }
    }
}
