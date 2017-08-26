using System;
using System.IO.Pipelines;
using System.Net;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal
{
    public abstract partial class TransportConnection
    {
        private readonly TaskCompletionSource<object> _inputTcs = new TaskCompletionSource<object>();
        private readonly TaskCompletionSource<object> _outputTcs = new TaskCompletionSource<object>();

        public TransportConnection()
        {
            _currentIConnectionIdFeature = this;
            _currentIConnectionTransportFeature = this;
            _currentIHttpConnectionFeature = this;
        }

        public IPAddress RemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public IPAddress LocalAddress { get; set; }
        public int LocalPort { get; set; }

        public string ConnectionId { get; set; }

        public virtual PipeFactory PipeFactory { get; }
        public virtual IScheduler InputWriterScheduler { get; }
        public virtual IScheduler OutputReaderScheduler { get; }

        public IPipeConnection Transport { get; set; }
        public IPipeConnection Application { get; set; }

        public IPipeWriter Input => Application.Output;
        public IPipeReader Output => Application.Input;

        protected void CloseInput(Exception exception)
        {
            if (exception == null)
            {
                _inputTcs.TrySetResult(null);
            }
            else
            {
                _inputTcs.TrySetException(exception);
            }

            Input.Complete(exception);
        }

        protected void CloseOutput(Exception exception)
        {
            if (exception == null)
            {
                _outputTcs.TrySetResult(null);
            }
            else
            {
                _outputTcs.TrySetException(exception);
            }

            Output.Complete(exception);
        }
    }
}
