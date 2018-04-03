// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.FunctionalTests;
using Microsoft.AspNetCore.Testing;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests
{
    public class HttpConnectionManagerTests : LoggedTest
    {
        public HttpConnectionManagerTests(ITestOutputHelper output) : base(output)
        {
        }

#if !DEBUG
        // This test causes MemoryPoolBlocks to be finalized which in turn causes an assert failure in debug builds.
        [ConditionalFact]
        [NoDebuggerCondition]
        public async Task CriticalErrorLoggedIfApplicationDoesntComplete()
        {
            ////////////////////////////////////////////////////////////////////////////////////////
            // WARNING: This test will fail under a debugger because Task.s_currentActiveTasks    //
            //          roots HttpConnection.                                                     //
            ////////////////////////////////////////////////////////////////////////////////////////

            using (StartLog(out var loggerFactory, TestConstants.DefaultFunctionalTestLogLevel))
            {

                var logWh = new SemaphoreSlim(0);
                var appStartedWh = new SemaphoreSlim(0);

                var mockTrace = new Mock<IKestrelTrace>();
                mockTrace
                    .Setup(trace => trace.ApplicationNeverCompleted(It.IsAny<string>()))
                    .Callback(() =>
                    {
                        logWh.Release();
                    });

                using (var server = new TestServer(context =>
                    {
                        appStartedWh.Release();
                        var tcs = new TaskCompletionSource<object>();
                        return tcs.Task;
                    },
                    new TestServiceContext(loggerFactory, mockTrace.Object)))
                {
                    using (var connection = server.CreateConnection())
                    {
                        await connection.SendEmptyGet();

                        Assert.True(await appStartedWh.WaitAsync(TestConstants.DefaultTimeout));

                        // Close connection without waiting for a response
                    }

                    var logWaitAttempts = 0;

                    for (; !await logWh.WaitAsync(TimeSpan.FromSeconds(1)) && logWaitAttempts < 30; logWaitAttempts++)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }

                    Assert.True(logWaitAttempts < 10);
                }
            }
        }
#endif

        private class NoDebuggerConditionAttribute : Attribute, ITestCondition
        {
            public bool IsMet => !Debugger.IsAttached;
            public string SkipReason => "A debugger is attached.";
        }
    }
}
