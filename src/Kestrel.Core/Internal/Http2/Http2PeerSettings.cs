// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2PeerSettings : IEnumerable<Http2PeerSetting>
    {
        public const uint DefaultHeaderTableSize = 4096;
        public const bool DefaultEnablePush = true;
        public const uint DefaultMaxConcurrentStreams = uint.MaxValue;
        public const uint DefaultInitialWindowSize = 65535;
        public const uint DefaultMaxFrameSize = 16384;
        public const uint DefaultMaxHeaderListSize = uint.MaxValue;

        public uint HeaderTableSize { get; set; } = DefaultHeaderTableSize;

        public bool EnablePush { get; set; } = DefaultEnablePush;

        public uint MaxConcurrentStreams { get; set; } = DefaultMaxConcurrentStreams;

        public uint InitialWindowSize { get; set; } = DefaultInitialWindowSize;

        public uint MaxFrameSize { get; set; } = DefaultMaxFrameSize;

        public uint MaxHeaderListSize { get; set; } = DefaultMaxHeaderListSize;

        public IEnumerator<Http2PeerSetting> GetEnumerator()
        {
            yield return new Http2PeerSetting(Http2SettingsFrameParameter.SETTINGS_HEADER_TABLE_SIZE, HeaderTableSize);
            yield return new Http2PeerSetting(Http2SettingsFrameParameter.SETTINGS_ENABLE_PUSH, EnablePush ? 1u : 0);
            yield return new Http2PeerSetting(Http2SettingsFrameParameter.SETTINGS_MAX_CONCURRENT_STREAMS, MaxConcurrentStreams);
            yield return new Http2PeerSetting(Http2SettingsFrameParameter.SETTINGS_INITIAL_WINDOW_SIZE, InitialWindowSize);
            yield return new Http2PeerSetting(Http2SettingsFrameParameter.SETTINGS_MAX_FRAME_SIZE, MaxFrameSize);
            yield return new Http2PeerSetting(Http2SettingsFrameParameter.SETTINGS_MAX_HEADER_LIST_SIZE, MaxHeaderListSize);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
