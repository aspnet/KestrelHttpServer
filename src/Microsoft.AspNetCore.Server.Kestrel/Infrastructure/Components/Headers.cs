// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Infrastructure
{
    public class Headers : IComponent
    {
        public static readonly byte[] BytesServer = Encoding.ASCII.GetBytes("\r\nServer: Kestrel");

        public readonly FrameRequestHeaders RequestHeaders = new FrameRequestHeaders();
        public readonly FrameResponseHeaders ResponseHeaders = new FrameResponseHeaders();

        public virtual void Initialize(DateHeaderValueManager dateValueManager)
        {
            var dateHeaderValues = dateValueManager.GetDateHeaderValues();
            ResponseHeaders.SetRawDate(dateHeaderValues.String, dateHeaderValues.Bytes);
            ResponseHeaders.SetRawServer("Kestrel", BytesServer);
        }

        public virtual void Reset()
        {
            RequestHeaders.Reset();
            ResponseHeaders.Reset();
        }

        public virtual void Uninitialize()
        {
            RequestHeaders.Reset();
            ResponseHeaders.Reset();
        }
    }
}
