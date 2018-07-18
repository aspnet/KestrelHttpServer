// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2StreamInputFlowControl
    {
        private readonly Http2InputFlowControl _connectionLevelFlowControl;
        private readonly Http2InputFlowControl _streamLevelFlowControl;

        private readonly int _streamId;
        private readonly Http2FrameWriter _frameWriter;

        public Http2StreamInputFlowControl(
            int streamId,
            Http2FrameWriter frameWriter,
            Http2InputFlowControl connectionLevelFlowControl,
            uint initialWindowSize)
        {
            _connectionLevelFlowControl = connectionLevelFlowControl;
            _streamLevelFlowControl = new Http2InputFlowControl(initialWindowSize);

            _streamId = streamId;
            _frameWriter = frameWriter;
        }

        public void Advance(int bytes)
        {
            var connectionSucess = _connectionLevelFlowControl.TryAdvance(bytes);

            Debug.Assert(connectionSucess, "Connection-level input flow control should never be aborted.");

            if (!_streamLevelFlowControl.TryAdvance(bytes))
            {
                // The stream has already been aborted, so immediately count the bytes as read at the connection level.
                UpdateConnectionWindow(bytes);
            }
        }

        public void UpdateWindows(int bytes)
        {
            if (!_streamLevelFlowControl.TryUpdateWindow(bytes, out var streamWindowUpdateSize))
            {
                // Stream-level flow control was aborted. Any unread bytes have already been returned to the connection
                // flow-control window by Abort().
                return;
            }

            if (streamWindowUpdateSize > 0)
            {
                // Writing with the FrameWriter should only fail if given a canceled token, so just fire and forget.
                _ = _frameWriter.WriteWindowUpdateAsync(_streamId, streamWindowUpdateSize);
            }

            UpdateConnectionWindow(bytes);
        }

        public void Abort()
        {
            var unreadBytes = _streamLevelFlowControl.Abort();

            if (unreadBytes > 0)
            {
                // We assume that the app won't read the remaining data from the request body pipe.
                // Even if the app does continue reading, _streamLevelFlowControl.TryUpdateWindow() will return false
                // from now on which prevents double counting.
                UpdateConnectionWindow(unreadBytes);
            }
        }

        private void UpdateConnectionWindow(int bytes)
        {
            var connectionSucess = _connectionLevelFlowControl.TryUpdateWindow(bytes, out var connectionWindowUpdateSize);

            Debug.Assert(connectionSucess, "Connection-level input flow control should never be aborted.");

            if (connectionWindowUpdateSize > 0)
            {
                // Writing with the FrameWriter should only fail if given a canceled token, so just fire and forget.
                _ = _frameWriter.WriteWindowUpdateAsync(0, connectionWindowUpdateSize);
            }
        }
    }
}
