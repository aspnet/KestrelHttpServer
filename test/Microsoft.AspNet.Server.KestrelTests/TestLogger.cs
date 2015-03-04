using Microsoft.Framework.Logging;
using System;

namespace Microsoft.AspNet.Server.KestrelTests
{
    public class TestLogger : ILogger
    {
        public string Name { get; set; }

        public IDisposable BeginScope(object state)
        {
            return null;
        }

        public void Write(LogLevel logLevel, int eventId, object state, Exception exception, Func<object, Exception, string> formatter)
        {

        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }
    }
}