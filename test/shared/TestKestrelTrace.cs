// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Testing
{
    public class TestKestrelTrace : KestrelTrace
    {
        public TestKestrelTrace() : this(new TestApplicationErrorLogger())
        {
        }

        public TestKestrelTrace(ILogger logger) : base(logger)
        {
            Logger = logger;
        }

        public ILogger Logger { get; private set; }

        public TestApplicationErrorLogger TestApplicationErrorLogger => (TestApplicationErrorLogger)Logger;

        public override void ConnectionRead(string connectionId, int count)
        {
            //_logger.LogDebug(1, @"Connection id ""{ConnectionId}"" recv {count} bytes.", connectionId, count);
        }

        public override void ConnectionWrite(string connectionId, int count)
        {
            //_logger.LogDebug(1, @"Connection id ""{ConnectionId}"" send {count} bytes.", connectionId, count);
        }

        public override void ConnectionWriteCallback(string connectionId, int status)
        {
            //_logger.LogDebug(1, @"Connection id ""{ConnectionId}"" send finished with status {status}.", connectionId, status);
        }
    }
}