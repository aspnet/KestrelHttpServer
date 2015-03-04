using Microsoft.Framework.Logging;

namespace Microsoft.AspNet.Server.KestrelTests
{
    public class TestLoggerFactory : ILoggerFactory
    {
        public LogLevel MinimumLevel { get; set; } = LogLevel.Verbose;

        public ILogger Create(string name)
        {
            return new TestLogger();
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }
    }
}