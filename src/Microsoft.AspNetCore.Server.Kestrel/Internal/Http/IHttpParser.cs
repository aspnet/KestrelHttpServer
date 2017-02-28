using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public interface IHttpParser
    {
        bool ParseStartLine<T>(T handler, ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined) where T : IHttpStartLineHandler;

        bool ParseHeaders<T>(T handler, ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined) where T : IHttpHeadersHandler;
    }
}