namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public enum HttpMethod: byte
    {
        Get,
        Put,
        Delete,
        Custom,
        Post,
        Head,
        Trace,
        Patch,
        Connect,
        Options
    }
}