using System.Diagnostics;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class BufferLengthControl : IBufferLengthControl
    {
        private readonly long _maxLength;
        private readonly IConnectionControl _connectionControl;
        private readonly KestrelThread _connectionThread;

        private readonly object _lock = new object();

        private long _length;
        private bool _connectionPaused;

        public BufferLengthControl(long maxLength, IConnectionControl connectionControl, KestrelThread connectionThread)
        {
            _maxLength = maxLength;
            _connectionControl = connectionControl;
            _connectionThread = connectionThread;
        }

        private long Length
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
            Debug.Assert(count >= 0);

            if (count == 0)
            {
                // No-op and avoid taking lock to reduce contention
                return;
            }

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
            Debug.Assert(count >= 0);

            if (count == 0)
            {
                // No-op and avoid taking lock to reduce contention
                return;
            }

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
