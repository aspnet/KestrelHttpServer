// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public abstract partial class Http2Stream : Frame
    {
        private readonly Http2StreamContext _context;

        public Http2Stream(Http2StreamContext context)
            : base(context)
        {
            _context = context;

            Output = new Http2OutputProducer(StreamId, _context.FrameWriter);
        }

        public int StreamId => _context.StreamId;

        public Http2MessageBody MessageBody { get; protected set; }

        protected IHttp2StreamLifetimeHandler StreamLifetimeHandler => _context.StreamLifetimeHandler;

        public bool ExpectBody { get; set; }

        public override bool IsUpgradableRequest => false;

        protected override string CreateRequestId()
            => StringUtilities.ConcatAsHexSuffix(ConnectionId, ':', (uint)StreamId);

        protected override void OnReset()
        {
            ExtraFeatureSet(typeof(IHttp2StreamIdFeature), this);
        }

        protected override Task WriteSuffix()
        {
            if (HttpMethods.IsHead(Method) && _responseBytesWritten > 0)
            {
                Log.ConnectionHeadResponseBodyWrite(ConnectionId, _responseBytesWritten);
            }

            return Output.WriteStreamSuffixAsync(default(CancellationToken));
        }
    }
}
