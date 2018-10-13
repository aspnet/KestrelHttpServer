// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2MessageBody : MessageBody
    {
        private readonly Http2Stream _context;
        private readonly ITimeoutControl _timeoutControl;

        private Http2MessageBody(Http2Stream context, ITimeoutControl timeoutControl)
            : base(context)
        {
            _context = context;
            _timeoutControl = timeoutControl;
        }

        protected override void OnReadStarting()
        {
            // Note ContentLength or MaxRequestBodySize may be null
            if (_context.RequestHeaders.ContentLength > _context.MaxRequestBodySize)
            {
                BadHttpRequestException.Throw(RequestRejectionReason.RequestBodyTooLarge);
            }
        }

        protected override void OnReadStarted()
        {
            // Produce 100-continue if no request body data for the stream has arrived yet.
            if (!_context.RequestBodyStarted)
            {
                TryProduceContinue();
            }
        }

        protected override void OnDataRead(int bytesRead)
        {
            _context.OnDataRead(bytesRead);
            AddAndCheckConsumedBytes(bytesRead);
        }

        protected override Task OnConsumeAsync() => Task.CompletedTask;

        public override Task StopAsync() => Task.CompletedTask;

        public static MessageBody For(Http2Stream context, ITimeoutControl timeoutControl)
        {
            if (context.EndStreamReceived && !context.RequestBodyStarted)
            {
                return ZeroContentLengthClose;
            }

            return new Http2MessageBody(context, timeoutControl);
        }
    }
}
