// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.KestrelTests.TestHelpers;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class SocketOutputTests
    {
        public static TheoryData<long?> MaxResponseBufferSizeData => new TheoryData<long?>
        {
            new KestrelServerOptions().Limits.MaxResponseBufferSize, 0, 1024, 1024 * 1024, null
        };

        public static TheoryData<int> PositiveMaxResponseBufferSizeData => new TheoryData<int>
        {
            (int)new KestrelServerOptions().Limits.MaxResponseBufferSize, 1024, (1024 * 1024) + 1
        };

        [Theory]
        [MemberData(nameof(MaxResponseBufferSizeData))]
        public async Task CanWrite1MB(long? maxResponseBufferSize)
        {
            // This test was added because when initially implementing write-behind buffering in
            // SocketOutput, the write callback would never be invoked for writes larger than
            // _maxBytesPreCompleted even after the write actually completed.

            // Arrange
            var mockLibuv = new MockLibuv();
            using (var kestrelEngine = new KestrelEngine(mockLibuv, new TestServiceContext()))
            using (var factory = new PipeFactory())
            {
                var kestrelThread = new KestrelThread(kestrelEngine, maxLoops: 1);
                kestrelEngine.Threads.Add(kestrelThread);
                await kestrelThread.StartAsync();

                var socket = new MockSocket(mockLibuv, kestrelThread.Loop.ThreadId, new TestKestrelTrace());
                var trace = new KestrelTrace(new TestKestrelTrace());

                // ListenerContext will set MaximumSizeHigh/Low to zero when MaxResponseBufferSize is null.
                // This is verified in ListenerContextTests.LibuvOutputPipeOptionsConfiguredCorrectly.
                var pipeOptions = new PipeOptions
                {
                    ReaderScheduler = kestrelThread,
                    MaximumSizeHigh = maxResponseBufferSize ?? 0,
                    MaximumSizeLow = maxResponseBufferSize ?? 0,
                };
                var pipe = factory.Create(pipeOptions);
                var socketOutput = new SocketOutput(pipe, kestrelThread, socket, new MockConnection(), "0", trace);

                // At least one run of this test should have a MaxResponseBufferSize < 1 MB.
                var bufferSize = 1024 * 1024;
                var buffer = new ArraySegment<byte>(new byte[bufferSize], 0, bufferSize);

                // Act
                var writeTask = socketOutput.WriteAsync(buffer, default(CancellationToken));

                // Assert
                await writeTask.TimeoutAfter(TimeSpan.FromSeconds(5));

                // Cleanup
                socketOutput.End(ProduceEndType.SocketDisconnect);
            }
        }

        [Fact]
        public async Task NullMaxResponseBufferSizeAllowsUnlimitedBuffer()
        {
            var completeQueue = new ConcurrentQueue<Action<int>>();

            // Arrange
            var mockLibuv = new MockLibuv
            {
                OnWrite = (socket, buffers, triggerCompleted) =>
                {
                    completeQueue.Enqueue(triggerCompleted);
                    return 0;
                }
            };

            using (var kestrelEngine = new KestrelEngine(mockLibuv, new TestServiceContext()))
            using (var factory = new PipeFactory())
            {
                var kestrelThread = new KestrelThread(kestrelEngine, maxLoops: 1);
                kestrelEngine.Threads.Add(kestrelThread);
                await kestrelThread.StartAsync();

                var socket = new MockSocket(mockLibuv, kestrelThread.Loop.ThreadId, new TestKestrelTrace());
                var trace = new KestrelTrace(new TestKestrelTrace());

                // ListenerContext will set MaximumSizeHigh/Low to zero when MaxResponseBufferSize is null.
                // This is verified in ListenerContextTests.LibuvOutputPipeOptionsConfiguredCorrectly.
                var pipeOptions = new PipeOptions
                {
                    ReaderScheduler = kestrelThread,
                    MaximumSizeHigh = 0,
                    MaximumSizeLow = 0,
                };
                var pipe = factory.Create(pipeOptions);
                var socketOutput = new SocketOutput(pipe, kestrelThread, socket, new MockConnection(), "0", trace);

                // Don't want to allocate anything too huge for perf. This is at least larger than the default buffer.
                var bufferSize = 1024 * 1024;
                var buffer = new ArraySegment<byte>(new byte[bufferSize], 0, bufferSize);

                // Act
                var writeTask = socketOutput.WriteAsync(buffer, default(CancellationToken));

                // Assert
                await writeTask.TimeoutAfter(TimeSpan.FromSeconds(5));

                // Cleanup
                socketOutput.End(ProduceEndType.SocketDisconnect);

                // Wait for all writes to complete so the completeQueue isn't modified during enumeration.
                await mockLibuv.OnPostTask;

                foreach (var triggerCompleted in completeQueue)
                {
                    await kestrelThread.PostAsync(cb => cb(0), triggerCompleted);
                }
            }
        }

        [Fact]
        public async Task ZeroMaxResponseBufferSizeDisablesBuffering()
        {
            var completeQueue = new ConcurrentQueue<Action<int>>();

            // Arrange
            var mockLibuv = new MockLibuv
            {
                OnWrite = (socket, buffers, triggerCompleted) =>
                {
                    completeQueue.Enqueue(triggerCompleted);
                    return 0;
                }
            };

            using (var kestrelEngine = new KestrelEngine(mockLibuv, new TestServiceContext()))
            using (var factory = new PipeFactory())
            {
                var kestrelThread = new KestrelThread(kestrelEngine, maxLoops: 1);
                kestrelEngine.Threads.Add(kestrelThread);
                await kestrelThread.StartAsync();

                var socket = new MockSocket(mockLibuv, kestrelThread.Loop.ThreadId, new TestKestrelTrace());
                var trace = new KestrelTrace(new TestKestrelTrace());

                // ListenerContext will set MaximumSizeHigh/Low to 1 when MaxResponseBufferSize is zero.
                // This is verified in ListenerContextTests.LibuvOutputPipeOptionsConfiguredCorrectly.
                var pipeOptions = new PipeOptions
                {
                    ReaderScheduler = kestrelThread,
                    MaximumSizeHigh = 1,
                    MaximumSizeLow = 1,
                };
                var pipe = factory.Create(pipeOptions);
                var socketOutput = new SocketOutput(pipe, kestrelThread, socket, new MockConnection(), "0", trace);

                var bufferSize = 1;
                var buffer = new ArraySegment<byte>(new byte[bufferSize], 0, bufferSize);

                // Act
                var writeTask = socketOutput.WriteAsync(buffer, default(CancellationToken));

                // Assert
                Assert.False(writeTask.IsCompleted);

                // Act
                await mockLibuv.OnPostTask;

                // Finishing the write should allow the task to complete.
                Assert.True(completeQueue.TryDequeue(out var triggerNextCompleted));
                await kestrelThread.PostAsync(cb => cb(0), triggerNextCompleted);

                // Assert
                await writeTask.TimeoutAfter(TimeSpan.FromSeconds(5));

                // Cleanup
                socketOutput.End(ProduceEndType.SocketDisconnect);

                // Wait for all writes to complete so the completeQueue isn't modified during enumeration.
                await mockLibuv.OnPostTask;

                foreach (var triggerCompleted in completeQueue)
                {
                    await kestrelThread.PostAsync(cb => cb(0), triggerCompleted);
                }
            }
        }

        [Theory]
        [MemberData(nameof(PositiveMaxResponseBufferSizeData))]
        public async Task WritesDontCompleteImmediatelyWhenTooManyBytesAreAlreadyBuffered(int maxResponseBufferSize)
        {
            var completeQueue = new ConcurrentQueue<Action<int>>();

            // Arrange
            var mockLibuv = new MockLibuv
            {
                OnWrite = (socket, buffers, triggerCompleted) =>
                {
                    completeQueue.Enqueue(triggerCompleted);
                    return 0;
                }
            };

            using (var kestrelEngine = new KestrelEngine(mockLibuv, new TestServiceContext()))
            using (var factory = new PipeFactory())
            {
                var kestrelThread = new KestrelThread(kestrelEngine, maxLoops: 1);
                kestrelEngine.Threads.Add(kestrelThread);
                await kestrelThread.StartAsync();

                var socket = new MockSocket(mockLibuv, kestrelThread.Loop.ThreadId, new TestKestrelTrace());
                var trace = new KestrelTrace(new TestKestrelTrace());
                var pipeOptions = new PipeOptions
                {
                    ReaderScheduler = kestrelThread,
                    MaximumSizeHigh = maxResponseBufferSize,
                    MaximumSizeLow = maxResponseBufferSize,
                };
                var pipe = factory.Create(pipeOptions);
                var socketOutput = new SocketOutput(pipe, kestrelThread, socket, new MockConnection(), "0", trace);

                var bufferSize = maxResponseBufferSize - 1;
                var buffer = new ArraySegment<byte>(new byte[bufferSize], 0, bufferSize);

                // Act 
                var writeTask1 = socketOutput.WriteAsync(buffer, default(CancellationToken));

                // Assert
                // The first write should pre-complete since it is <= _maxBytesPreCompleted.
                Assert.Equal(TaskStatus.RanToCompletion, writeTask1.Status);

                // Act
                var writeTask2 = socketOutput.WriteAsync(buffer, default(CancellationToken));
                await mockLibuv.OnPostTask;

                // Assert 
                // Too many bytes are already pre-completed for the second write to pre-complete.
                Assert.False(writeTask2.IsCompleted);

                // Act
                Assert.True(completeQueue.TryDequeue(out var triggerNextCompleted));
                await kestrelThread.PostAsync(cb => cb(0), triggerNextCompleted);

                // Finishing the first write should allow the second write to pre-complete.
                await writeTask2.TimeoutAfter(TimeSpan.FromSeconds(5));

                // Cleanup
                socketOutput.End(ProduceEndType.SocketDisconnect);

                // Wait for all writes to complete so the completeQueue isn't modified during enumeration.
                await mockLibuv.OnPostTask;

                foreach (var triggerCompleted in completeQueue)
                {
                    await kestrelThread.PostAsync(cb => cb(0), triggerCompleted);
                }
            }
        }

        [Theory]
        [MemberData(nameof(PositiveMaxResponseBufferSizeData))]
        public async Task WritesDontCompleteImmediatelyWhenTooManyBytesIncludingNonImmediateAreAlreadyBuffered(int maxResponseBufferSize)
        {
            var completeQueue = new ConcurrentQueue<Action<int>>();

            // Arrange
            var mockLibuv = new MockLibuv
            {
                OnWrite = (socket, buffers, triggerCompleted) =>
                {
                    completeQueue.Enqueue(triggerCompleted);
                    return 0;
                }
            };

            using (var kestrelEngine = new KestrelEngine(mockLibuv, new TestServiceContext()))
            using (var factory = new PipeFactory())
            {
                var kestrelThread = new KestrelThread(kestrelEngine, maxLoops: 1);
                kestrelEngine.Threads.Add(kestrelThread);
                await kestrelThread.StartAsync();

                var socket = new MockSocket(mockLibuv, kestrelThread.Loop.ThreadId, new TestKestrelTrace());
                var trace = new KestrelTrace(new TestKestrelTrace());
                var pipeOptions = new PipeOptions
                {
                    ReaderScheduler = kestrelThread,
                    MaximumSizeHigh = maxResponseBufferSize,
                    MaximumSizeLow = maxResponseBufferSize,
                };
                var pipe = factory.Create(pipeOptions);
                var socketOutput = new SocketOutput(pipe, kestrelThread, socket, new MockConnection(), "0", trace);

                var bufferSize = maxResponseBufferSize / 2;
                var data = new byte[bufferSize];
                var halfWriteBehindBuffer = new ArraySegment<byte>(data, 0, bufferSize);

                // Act 
                var writeTask1 = socketOutput.WriteAsync(halfWriteBehindBuffer, default(CancellationToken));

                // Assert
                // The first write should pre-complete since it is <= _maxBytesPreCompleted.
                Assert.Equal(TaskStatus.RanToCompletion, writeTask1.Status);
                await mockLibuv.OnPostTask;
                Assert.NotEmpty(completeQueue);

                // Add more bytes to the write-behind buffer to prevent the next write from
                socketOutput.Write((writableBuffer, state) =>
                {
                    writableBuffer.Write(state);
                },
                halfWriteBehindBuffer);

                // Act
                var writeTask2 = socketOutput.WriteAsync(halfWriteBehindBuffer, default(CancellationToken));
                Assert.False(writeTask2.IsCompleted);

                var writeTask3 = socketOutput.WriteAsync(halfWriteBehindBuffer, default(CancellationToken));
                Assert.False(writeTask3.IsCompleted);

                // Drain the write queue
                while (completeQueue.TryDequeue(out var triggerNextCompleted))
                {
                    await kestrelThread.PostAsync(cb => cb(0), triggerNextCompleted);
                }

                var timeout = TimeSpan.FromSeconds(5);

                await writeTask2.TimeoutAfter(timeout);
                await writeTask3.TimeoutAfter(timeout);

                Assert.Empty(completeQueue);

                // Cleanup
                socketOutput.End(ProduceEndType.SocketDisconnect);
            }
        }

        [Theory]
        [MemberData(nameof(PositiveMaxResponseBufferSizeData))]
        public async Task FailedWriteCompletesOrCancelsAllPendingTasks(int maxResponseBufferSize)
        {
            var completeQueue = new ConcurrentQueue<Action<int>>();

            // Arrange
            var mockLibuv = new MockLibuv
            {
                OnWrite = (socket, buffers, triggerCompleted) =>
                {
                    completeQueue.Enqueue(triggerCompleted);
                    return 0;
                }
            };

            using (var kestrelEngine = new KestrelEngine(mockLibuv, new TestServiceContext()))
            using (var factory = new PipeFactory())
            {
                var kestrelThread = new KestrelThread(kestrelEngine, maxLoops: 1);
                kestrelEngine.Threads.Add(kestrelThread);
                await kestrelThread.StartAsync();

                var socket = new MockSocket(mockLibuv, kestrelThread.Loop.ThreadId, new TestKestrelTrace());
                var trace = new KestrelTrace(new TestKestrelTrace());

                using (var mockConnection = new MockConnection())
                {
                    var pipeOptions = new PipeOptions
                    {
                        ReaderScheduler = kestrelThread,
                        MaximumSizeHigh = maxResponseBufferSize,
                        MaximumSizeLow = maxResponseBufferSize,
                    };
                    var pipe = factory.Create(pipeOptions);
                    var abortedSource = mockConnection.RequestAbortedSource;
                    var socketOutput = new SocketOutput(pipe, kestrelThread, socket, mockConnection, "0", trace);

                    var bufferSize = maxResponseBufferSize - 1;

                    var data = new byte[bufferSize];
                    var fullBuffer = new ArraySegment<byte>(data, 0, bufferSize);

                    // Act 
                    var task1Success = socketOutput.WriteAsync(fullBuffer, cancellationToken: abortedSource.Token);
                    // task1 should complete successfully as < _maxBytesPreCompleted

                    // First task is completed and successful
                    Assert.True(task1Success.IsCompleted);
                    Assert.False(task1Success.IsCanceled);
                    Assert.False(task1Success.IsFaulted);

                    // following tasks should wait.
                    var task2Success = socketOutput.WriteAsync(fullBuffer, cancellationToken: default(CancellationToken));
                    var task3Canceled = socketOutput.WriteAsync(fullBuffer, cancellationToken: abortedSource.Token);

                    // Give time for tasks to percolate
                    await mockLibuv.OnPostTask;

                    // Second task is not completed
                    Assert.False(task2Success.IsCompleted);
                    Assert.False(task2Success.IsCanceled);
                    Assert.False(task2Success.IsFaulted);

                    // Third task is not completed 
                    Assert.False(task3Canceled.IsCompleted);
                    Assert.False(task3Canceled.IsCanceled);
                    Assert.False(task3Canceled.IsFaulted);


                    // Cause all writes to fail
                    while (completeQueue.TryDequeue(out var triggerNextCompleted))
                    {
                        await kestrelThread.PostAsync(cb => cb(-1), triggerNextCompleted);
                    }

                    // Second task is now completed
                    await task2Success.TimeoutAfter(TimeSpan.FromSeconds(5));

                    // Third task is now canceled
                    // TODO: Cancellation isn't supported right now
                    // await Assert.ThrowsAsync<TaskCanceledException>(() => task3Canceled);
                    // Assert.True(task3Canceled.IsCanceled);

                    Assert.True(abortedSource.IsCancellationRequested);

                    // Cleanup
                    socketOutput.End(ProduceEndType.SocketDisconnect);
                }
            }
        }

        [Theory]
        [MemberData(nameof(PositiveMaxResponseBufferSizeData))]
        public async Task WritesDontGetCompletedTooQuickly(int maxResponseBufferSize)
        {
            var completeQueue = new ConcurrentQueue<Action<int>>();

            // Arrange
            var mockLibuv = new MockLibuv
            {
                OnWrite = (socket, buffers, triggerCompleted) =>
                {
                    completeQueue.Enqueue(triggerCompleted);
                    return 0;
                }
            };

            using (var kestrelEngine = new KestrelEngine(mockLibuv, new TestServiceContext()))
            using (var factory = new PipeFactory())
            {
                var kestrelThread = new KestrelThread(kestrelEngine, maxLoops: 1);
                kestrelEngine.Threads.Add(kestrelThread);
                await kestrelThread.StartAsync();

                var socket = new MockSocket(mockLibuv, kestrelThread.Loop.ThreadId, new TestKestrelTrace());
                var trace = new KestrelTrace(new TestKestrelTrace());
                var pipeOptions = new PipeOptions
                {
                    ReaderScheduler = kestrelThread,
                    MaximumSizeHigh = maxResponseBufferSize,
                    MaximumSizeLow = maxResponseBufferSize,
                };
                var pipe = factory.Create(pipeOptions);
                var socketOutput = new SocketOutput(pipe, kestrelThread, socket, new MockConnection(), "0", trace);

                var bufferSize = maxResponseBufferSize - 1;
                var buffer = new ArraySegment<byte>(new byte[bufferSize], 0, bufferSize);

                // Act (Pre-complete the maximum number of bytes in preparation for the rest of the test)
                var writeTask1 = socketOutput.WriteAsync(buffer, default(CancellationToken));

                // Assert
                // The first write should pre-complete since it is < _maxBytesPreCompleted.
                await mockLibuv.OnPostTask;
                Assert.Equal(TaskStatus.RanToCompletion, writeTask1.Status);
                Assert.NotEmpty(completeQueue);

                // Act
                var writeTask2 = socketOutput.WriteAsync(buffer, default(CancellationToken));
                var writeTask3 = socketOutput.WriteAsync(buffer, default(CancellationToken));

                // Drain the write queue
                while (completeQueue.TryDequeue(out var triggerNextCompleted))
                {
                    await kestrelThread.PostAsync(cb => cb(0), triggerNextCompleted);
                }

                var timeout = TimeSpan.FromSeconds(5);

                // Assert 
                // Too many bytes are already pre-completed for the third but not the second write to pre-complete.
                // https://github.com/aspnet/KestrelHttpServer/issues/356
                await writeTask2.TimeoutAfter(timeout);
                await writeTask3.TimeoutAfter(timeout);

                // Cleanup
                socketOutput.End(ProduceEndType.SocketDisconnect);
            }
        }

        [Theory]
        [MemberData(nameof(MaxResponseBufferSizeData))]
        public async Task WritesAreAggregated(long? maxResponseBufferSize)
        {
            var writeCalled = false;
            var writeCount = 0;

            var mockLibuv = new MockLibuv
            {
                OnWrite = (socket, buffers, triggerCompleted) =>
                {
                    writeCount++;
                    triggerCompleted(0);
                    writeCalled = true;
                    return 0;
                }
            };

            using (var kestrelEngine = new KestrelEngine(mockLibuv, new TestServiceContext()))
            using (var factory = new PipeFactory())
            {
                var kestrelThread = new KestrelThread(kestrelEngine, maxLoops: 1);
                kestrelEngine.Threads.Add(kestrelThread);
                await kestrelThread.StartAsync();

                var socket = new MockSocket(mockLibuv, kestrelThread.Loop.ThreadId, new TestKestrelTrace());
                var trace = new KestrelTrace(new TestKestrelTrace());

                // ListenerContext will set MaximumSizeHigh/Low to zero when MaxResponseBufferSize is null.
                // This is verified in ListenerContextTests.LibuvOutputPipeOptionsConfiguredCorrectly.
                var pipeOptions = new PipeOptions
                {
                    ReaderScheduler = kestrelThread,
                    MaximumSizeHigh = maxResponseBufferSize ?? 0,
                    MaximumSizeLow = maxResponseBufferSize ?? 0,
                };
                var pipe = factory.Create(pipeOptions);
                var socketOutput = new SocketOutput(pipe, kestrelThread, socket, new MockConnection(), "0", trace);

                mockLibuv.KestrelThreadBlocker.Reset();

                var buffer = new ArraySegment<byte>(new byte[1]);

                // Two calls to WriteAsync trigger uv_write once if both calls
                // are made before write is scheduled
                var ignore = socketOutput.WriteAsync(buffer, CancellationToken.None);
                ignore = socketOutput.WriteAsync(buffer, CancellationToken.None);

                mockLibuv.KestrelThreadBlocker.Set();

                await mockLibuv.OnPostTask;

                Assert.True(writeCalled);
                writeCalled = false;

                // Write isn't called twice after the thread is unblocked
                await mockLibuv.OnPostTask;

                Assert.False(writeCalled);
                // One call to ScheduleWrite
                Assert.Equal(1, mockLibuv.PostCount);
                // One call to uv_write
                Assert.Equal(1, writeCount);

                // Cleanup
                socketOutput.End(ProduceEndType.SocketDisconnect);
            }
        }

        [Fact]
        public async Task AllocCommitCanBeCalledAfterConnectionClose()
        {
            var mockLibuv = new MockLibuv();

            using (var kestrelEngine = new KestrelEngine(mockLibuv, new TestServiceContext()))
            using (var factory = new PipeFactory())
            {
                var kestrelThread = new KestrelThread(kestrelEngine, maxLoops: 1);
                kestrelEngine.Threads.Add(kestrelThread);
                await kestrelThread.StartAsync();

                var socket = new MockSocket(mockLibuv, kestrelThread.Loop.ThreadId, new TestKestrelTrace());
                var trace = new KestrelTrace(new TestKestrelTrace());
                var connection = new MockConnection();
                var pipeOptions = new PipeOptions
                {
                    ReaderScheduler = kestrelThread,
                };
                var pipe = factory.Create(pipeOptions);
                var socketOutput = new SocketOutput(pipe, kestrelThread, socket, connection, "0", trace);

                // Close SocketOutput
                socketOutput.End(ProduceEndType.SocketDisconnect);

                await mockLibuv.OnPostTask;

                Assert.Equal(TaskStatus.RanToCompletion, connection.SocketClosed.Status);

                var called = false;

                socketOutput.Write<object>((buffer, state) =>
                {
                    called = true;
                }, 
                null);

                Assert.False(called);
            }
        }
    }
}