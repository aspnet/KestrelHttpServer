﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Protocols.Features;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Tests.TestHelpers
{
    public class MockConnectionHandler : IConnectionHandler
    {
        public PipeOptions InputOptions { get; set; } = new PipeOptions();
        public PipeOptions OutputOptions { get; set; } = new PipeOptions();

        public void OnConnection(IFeatureCollection features)
        {
            var connectionContext = new DefaultConnectionContext(features);

            Input = connectionContext.PipeFactory.Create(InputOptions ?? new PipeOptions());
            Output = connectionContext.PipeFactory.Create(OutputOptions ?? new PipeOptions());

            var context = new TestConnectionContext
            {
                Connection = new PipeConnection(Output.Reader, Input.Writer)
            };

            connectionContext.Features.Set<IConnectionApplicationFeature>(context);
        }

        public IPipe Input { get; private set; }
        public IPipe Output { get; private set; }

        private class TestConnectionContext : IConnectionApplicationFeature
        {
            public string ConnectionId { get; }
            public IPipeConnection Connection { get; set; }

            public void Abort(Exception ex)
            {
            }

            public void OnConnectionClosed(Exception ex)
            {
            }
        }
    }
}
