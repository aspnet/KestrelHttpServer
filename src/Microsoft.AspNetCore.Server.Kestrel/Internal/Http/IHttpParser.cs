using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public interface IHttpParser
    {
        bool ParseRequestLine<T>(T handler, ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined) where T : IHttpRequestLineHandler;

        bool ParseHeaders<T>(T handler, ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined, out int consumedBytes) where T : IHttpHeadersHandler;
    }
}