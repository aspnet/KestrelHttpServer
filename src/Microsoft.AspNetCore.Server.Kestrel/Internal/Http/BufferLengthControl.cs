using System.Diagnostics;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class BufferLengthControl : IBufferLengthControl
    {
        private readonly int _maxLength;
        private readonly IConnectionControl _connectionControl;
        private readonly KestrelThread _connectionThread;

        private readonly object _lock = new object();

        private int _length;
        private bool _connectionPaused;

        public BufferLengthControl(int maxLength, IConnectionControl connectionControl, KestrelThread connectionThread)
        {
            _maxLength = maxLength;
            _connectionControl = connectionControl;
            _connectionThread = connectionThread;
        }

        private int Length
        {
            get
            {
                return _length;
            }
            set
            {
                // Caller should ensure that bytes are never consumed before the producer has called Add()
                Debug.Assert(value >= 0);
                _length = value;
            }
        }

        public void Add(int count)
        {
            lock (_lock)
            {
                Length += count;
                if (!_connectionPaused && Length >= _maxLength)
                {
                    _connectionPaused = true;
                    _connectionThread.Post(
                        (connectionControl) => ((IConnectionControl)connectionControl).Pause(),
                        _connectionControl);
                }
            }
        }

        public void Subtract(int count)
        {
            lock (_lock)
            {
                Length -= count;
                if (_connectionPaused && Length < _maxLength)
                {
                    _connectionPaused = false;
                    _connectionThread.Post(
                        (connectionControl) => ((IConnectionControl)connectionControl).Resume(),
                        _connectionControl);
                }
            }
        }
    }
}
