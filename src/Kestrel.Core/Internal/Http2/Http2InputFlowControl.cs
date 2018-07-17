// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2InputFlowControl
    {
        private readonly Http2FrameWriter _frameWriter;
        private readonly int _streamId;
        private readonly uint _minWindowSizeIncrement;

        private int _unconfirmedBytesRead;

        public Http2InputFlowControl(
            Http2FrameWriter frameWriter,
            int streamId,
            uint minWindowSizeIncrement)
        {
            Debug.Assert(minWindowSizeIncrement <= Http2PeerSettings.MaxWindowSize, $"{nameof(minWindowSizeIncrement)} too large.");

            _frameWriter = frameWriter;
            _streamId = streamId;
            _minWindowSizeIncrement = minWindowSizeIncrement;
        }


        public void OnDataRead(int bytes)
        {
            _unconfirmedBytesRead += bytes;

            if (_unconfirmedBytesRead > _minWindowSizeIncrement)
            {
                _frameWriter.WriteWindowUpdateAsync(_streamId, _unconfirmedBytesRead);
                _unconfirmedBytesRead = 0;
            }
        }
    }
}
