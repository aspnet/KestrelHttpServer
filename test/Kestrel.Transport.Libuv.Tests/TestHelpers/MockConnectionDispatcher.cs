﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Tests.TestHelpers
{
    public class MockConnectionDispatcher : IConnectionDispatcher
    {
        public Func<MemoryPool<byte>, PipeOptions> InputOptions { get; set; } = pool => new PipeOptions(pool, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false);
        public Func<MemoryPool<byte>, PipeOptions> OutputOptions { get; set; } = pool => new PipeOptions(pool, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false);

        public void OnConnection(TransportConnection connection)
        {
            Input = new Pipe(InputOptions(connection.MemoryPool));
            Output = new Pipe(OutputOptions(connection.MemoryPool));

            connection.Transport = new DuplexPipe(Input.Reader, Output.Writer);
            connection.Application = new DuplexPipe(Output.Reader, Input.Writer);
        }

        public Pipe Input { get; private set; }
        public Pipe Output { get; private set; }
    }
}
