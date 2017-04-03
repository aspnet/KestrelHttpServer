// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal;

namespace Microsoft.AspNetCore.Testing
{
    public class TestKestrelTrace : KestrelTrace
    {
        public TestKestrelTrace() : this(new TestApplicationErrorLogger())
        {
        }

        public TestKestrelTrace(TestApplicationErrorLogger testLogger) : base(testLogger)
        {
            Logger = testLogger;
        }

        public TestApplicationErrorLogger Logger { get; private set; }
    }
}