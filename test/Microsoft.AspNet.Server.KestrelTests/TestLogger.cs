﻿using System;
using Microsoft.AspNet.Server.Kestrel;
using Microsoft.Framework.Logging;

namespace Microsoft.AspNet.Server.KestrelTests
{
    public class TestKestrelTrace : KestrelTrace
    {
        public TestKestrelTrace() : base(new TestLogger())
        {

        }

        public override void ConnectionRead(long connectionId, int count)
        {
            _logger.LogDebug(1, @"Connection id ""{ConnectionId}"" recv {count} bytes.", connectionId, count);
        }

        public override void ConnectionWrite(long connectionId, int count)
        {
            _logger.LogDebug(1, @"Connection id ""{ConnectionId}"" send {count} bytes.", connectionId, count);
        }

        public override void ConnectionWriteCallback(long connectionId, int status)
        {
            _logger.LogDebug(1, @"Connection id ""{ConnectionId}"" send finished with status {status}.", connectionId, status);
        }

        public class TestLogger : ILogger
        {
            public void Log(LogLevel logLevel, int eventId, object state, Exception exception, Func<object, Exception, string> formatter)
            {
                Console.WriteLine($"Log {logLevel}[{eventId}]: {formatter(state, exception)} {exception?.Message}");
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public IDisposable BeginScopeImpl(object state)
            {
                return new Disposable(() => { });
            }
        }
    }
}