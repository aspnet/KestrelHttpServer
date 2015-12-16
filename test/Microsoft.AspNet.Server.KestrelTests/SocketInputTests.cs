﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel;
using Microsoft.AspNet.Server.Kestrel.Http;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Xunit;

namespace Microsoft.AspNet.Server.KestrelTests
{
    public class SocketInputTests
    {
        [Fact]
        public async Task ConcurrentReadsFailGracefully()
        {
            // Arrange
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            var memory2 = new MemoryPool2();
            var socketInput = new SocketInput(memory2, ltp);

            var task0Threw = false;
            var task1Threw = false;
            var task2Threw = false;


            var task0 = AwaitAsTaskAsync(socketInput);

            Assert.False(task0.IsFaulted);

            var task = task0.ContinueWith(
                (t) =>
                {
                    TestConcurrentFaultedTask(t);
                    task0Threw = true;
                },
                TaskContinuationOptions.OnlyOnFaulted);

            Assert.False(task0.IsFaulted);

            // Awaiting/continuing two tasks faults both

            var task1 = AwaitAsTaskAsync(socketInput);

            await task1.ContinueWith(
                (t) =>
                {
                    TestConcurrentFaultedTask(t);
                    task1Threw = true;
                },
                TaskContinuationOptions.OnlyOnFaulted);

            await task;

            Assert.True(task0.IsFaulted);
            Assert.True(task1.IsFaulted);

            Assert.True(task0Threw);
            Assert.True(task1Threw);

            // socket stays faulted

            var task2 = AwaitAsTaskAsync(socketInput);

            await task2.ContinueWith(
                (t) =>
                {
                    TestConcurrentFaultedTask(t);
                    task2Threw = true;
                },
                TaskContinuationOptions.OnlyOnFaulted);

            Assert.True(task2.IsFaulted);
            Assert.True(task2Threw);
        }

        private static void TestConcurrentFaultedTask(Task t)
        {
            Assert.True(t.IsFaulted);
            Assert.IsType(typeof(System.IO.IOException), t.Exception.InnerException);
            Assert.Equal(t.Exception.InnerException.Message, "Concurrent reads are not supported.");
        }

        private async Task AwaitAsTaskAsync(SocketInput socketInput)
        {
            await socketInput;
        }

    }
}
