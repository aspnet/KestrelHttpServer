namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    public interface IStringCache
    {
        void MarkStart();
        unsafe string GetString(char* data, uint hash, int length);
    }
}