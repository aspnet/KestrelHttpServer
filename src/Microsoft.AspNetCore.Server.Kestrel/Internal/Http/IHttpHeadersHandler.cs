using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public interface IHttpHeadersHandler
    {
        void OnHeader(Span<byte> name, Span<byte> value);
    }
}