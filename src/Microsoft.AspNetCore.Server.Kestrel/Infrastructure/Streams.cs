// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Infrastructure
{
    internal class Streams
    {
        public readonly FrameRequestStream RequestBody;
        public readonly FrameResponseStream ResponseBody;
        public readonly FrameDuplexStream DuplexStream;

        public Streams()
        {
            RequestBody = new FrameRequestStream();
            ResponseBody = new FrameResponseStream();
            DuplexStream = new FrameDuplexStream(RequestBody, ResponseBody);
        }

        public void Initialize(FrameContext renter)
        {
            ResponseBody.Initialize(renter);
        }

        public void Uninitialize()
        {
            ResponseBody.Uninitialize();
        }
    }
}