using System;
using Microsoft.AspNet.Server.Kestrel.Http;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.KestrelTests
{
    class TestInput : IConnectionControl, IFrameControl
    {
        public TestInput()
        {
            var memory = new MemoryPool();
            FrameContext = new FrameContext
            {
                SocketInput = new SocketInput(memory),
                Memory = memory,
                ConnectionControl = this,
                FrameControl = this
            };
        }

        public FrameContext FrameContext { get; set; }

        public void Add(string text, bool fin = false)
        {
            var encoding = System.Text.Encoding.ASCII;
            var count = encoding.GetByteCount(text);
            var buffer = FrameContext.SocketInput.Available(text.Length);
            count = encoding.GetBytes(text, 0, text.Length, buffer.Array, buffer.Offset);
            FrameContext.SocketInput.Extend(count);
            if (fin)
            {
                FrameContext.SocketInput.RemoteIntakeFin = true;
            }
        }

        public Task ProduceContinueAsync()
        {
            return Task.CompletedTask;
        }

        public void Pause()
        {
        }

        public void Resume()
        {
        }

        public Task WriteAsync(ArraySegment<byte> data, Action<Exception, object> callback, object state)
        {
            return Task.CompletedTask;
        }
        public Task EndAsync(ProduceEndType endType)
        {
            return Task.CompletedTask;
        }

        public bool IsInKeepAlive => false;
    }
}

