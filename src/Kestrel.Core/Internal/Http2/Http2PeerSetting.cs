// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public struct Http2PeerSetting
    {
        public Http2PeerSetting(Http2SettingsFrameParameter parameter, uint value)
        {
            Parameter = parameter;
            Value = value;
        }

        public Http2SettingsFrameParameter Parameter { get; }

        public uint Value { get; }
    }
}
