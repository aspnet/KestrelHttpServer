using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public interface IHttpStartLineHandler
    {
        void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod);
    }
}