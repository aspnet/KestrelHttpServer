// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Infrastructure
{
    internal class Headers
    {
        public static readonly byte[] BytesServer = Encoding.ASCII.GetBytes("\r\nServer: Kestrel");

        public readonly FrameRequestHeaders RequestHeaders = new FrameRequestHeaders();
        public readonly FrameResponseHeaders ResponseHeaders = new FrameResponseHeaders();

        public void Initialize(DateHeaderValueManager dateValueManager)
        {
            ResponseHeaders.SetRawDate(
                dateValueManager.GetDateHeaderValue(),
                dateValueManager.GetDateHeaderValueBytes());
            ResponseHeaders.SetRawServer("Kestrel", BytesServer);
        }

        public void Uninitialize()
        {
            RequestHeaders.Reset();
            ResponseHeaders.Reset();
        }
    }
}