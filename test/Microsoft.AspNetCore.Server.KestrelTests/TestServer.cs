// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Http;
using Microsoft.AspNetCore.Server.Kestrel.TestCommon;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    /// <summary>
    /// Summary description for TestServer
    /// </summary>
    public class TestServer : IDisposable
    {
        private KestrelEngine _engine;
        private IDisposable _server;
        ServerAddress _address;

        public TestServer(RequestDelegate app)
            : this(app, new TestServiceContext())
        {
        }

        public TestServer(RequestDelegate app, ServiceContext context)
            : this(app, context, "http://localhost:0/")
        {
        }

        public int Port => _address.Port;

        public TestServer(RequestDelegate app, ServiceContext context, string serverAddress)
        {
            context.FrameFactory = connectionContext =>
            {
                return new Frame<HttpContext>(new DummyApplication(app), connectionContext);
            };

            try
            {
                _engine = new KestrelEngine(context);
                _engine.Start(1);
                _address = ServerAddress.FromUrl(serverAddress);
                _server = _engine.CreateServer(_address);
            }
            catch
            {
                _server?.Dispose();
                _engine?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _server.Dispose();
            _engine.Dispose();
        }
    }
}