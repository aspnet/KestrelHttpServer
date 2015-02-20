namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public abstract class UvTcpHandle : UvLoopResource
    {
        protected UvTcpHandle(UvLoopHandle loop)
            :base(loop.ThreadId, getSize())
        {
            loop.Validate();
            Validate();

            Libuv.ThrowOnError(UnsafeNativeMethods.uv_tcp_init(loop, Handle));
        }

        private static int getSize()
        {
            return UnsafeNativeMethods.uv_handle_size(HandleType.TCP);
        }
    }
}