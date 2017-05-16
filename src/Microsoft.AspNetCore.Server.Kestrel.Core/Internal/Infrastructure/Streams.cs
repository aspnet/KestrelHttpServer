﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    internal class Streams
    {
        private static readonly ThrowingWriteOnlyStream _throwingResponseStream
            = new ThrowingWriteOnlyStream(new InvalidOperationException(CoreStrings.ResponseStreamWasUpgraded));
        private readonly FrameResponseStream _response;
        private readonly FrameRequestStream _request;
        private readonly WrappingStream _upgradeableResponse;
        private readonly FrameRequestStream _emptyRequest;
        private readonly Stream _upgradeStream;

        public Streams(IFrameControl frameControl)
        {
            _request = new FrameRequestStream();
            _emptyRequest = new FrameRequestStream();
            _response = new FrameResponseStream(frameControl);
            _upgradeableResponse = new WrappingStream(_response);
            _upgradeStream = new FrameDuplexStream(_request, _response);
        }

        public Stream Upgrade()
        {
            // causes writes to context.Response.Body to throw
            _upgradeableResponse.SetInnerStream(_throwingResponseStream);
            // _upgradeStream always uses _response
            return _upgradeStream;
        }

        public (Stream request, Stream response) Start(IRequestBodyReader requestBodyReader, bool requestUpgradable)
        {
            _request.StartAcceptingReads(requestBodyReader);
            _emptyRequest.StartAcceptingReads(EmptyRequestBodyReader.Instance);
            _response.StartAcceptingWrites();

            if (requestUpgradable)
            {
                // until Upgrade() is called, context.Response.Body should use the normal output stream
                _upgradeableResponse.SetInnerStream(_response);
                // upgradeable requests should never have a request body
                return (_emptyRequest, _upgradeableResponse);
            }
            else
            {
                return (_request, _response);
            }
        }

        public void Pause()
        {
            _request.PauseAcceptingReads();
            _emptyRequest.PauseAcceptingReads();
            _response.PauseAcceptingWrites();
        }

        public void Resume()
        {
            _request.ResumeAcceptingReads();
            _emptyRequest.ResumeAcceptingReads();
            _response.ResumeAcceptingWrites();
        }

        public void Stop()
        {
            _request.StopAcceptingReads();
            _emptyRequest.StopAcceptingReads();
            _response.StopAcceptingWrites();
        }

        public void Abort(Exception error)
        {
            _request.Abort(error);
            _emptyRequest.Abort(error);
            _response.Abort();
        }
    }
}
