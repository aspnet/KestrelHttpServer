// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public abstract class Http2MessageBody : MessageBody
    {
        private readonly Http2Stream _context;

        protected Http2MessageBody(Http2Stream context)
            : base(context)
        {
            _context = context;
        }

        public override ValueTask<int> ReadAsync(System.Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            TryInit();
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            TryInit();
            return base.CopyToAsync(destination, cancellationToken);
        }

        private void TryInit()
        {
            if (!_context.HasStartedConsumingRequestBody)
            {
                _context.HasStartedConsumingRequestBody = true;

                // Produce 100-continue if no request body data for the stream has arrived yet.
                if (!_context.RequestBodyStarted)
                {
                    TryProduceContinue();
                }
            }
        }

        public override void Stop()
        {
            _context.RequestBodyPipe.Writer.Complete();
            _context.RequestBodyPipe.Reset();
        }

        public static MessageBody For(
            HttpRequestHeaders headers,
            Http2Stream context)
        {
            if (context.EndStreamReceived)
            {
                return ZeroContentLengthClose;
            }

            return new ForHttp2(context);
        }

        private class ForHttp2 : Http2MessageBody
        {
            public ForHttp2(Http2Stream context)
                : base(context)
            {
            }
        }
    }
}
