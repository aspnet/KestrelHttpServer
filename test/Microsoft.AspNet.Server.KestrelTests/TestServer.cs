﻿using Microsoft.AspNet.Server.Kestrel;
using Microsoft.AspNet.Server.Kestrel.Http;
using Microsoft.Framework.Runtime;
using Microsoft.Framework.Runtime.Infrastructure;
using System;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.KestrelTests
{
    /// <summary>
    /// Summary description for TestServer
    /// </summary>
    public class TestServer : IDisposable
    {
        private KestrelEngine _engine;
        private IDisposable _server;

        public TestServer(Func<Frame, Task> app, int port)
        {
            Create(app, port);
        }

        ILibraryManager LibraryManager
        {
            get
            {
                try{
                    var locator = CallContextServiceLocator.Locator;
                    if (locator == null) 
                    {
                        return null;
                    }
                    var services = locator.ServiceProvider;
                    if (services == null) 
                    {
                        return null;
                    }
                    return (ILibraryManager)services.GetService(typeof(ILibraryManager));
                } catch (NullReferenceException) { return null; }
            }
        }

        public void Create(Func<Frame, Task> app, int port)
        {
            _engine = new KestrelEngine(LibraryManager);
            _engine.Start(1);
            _server = _engine.CreateServer(
                "http",
                "localhost",
                port,
                app);
        }

        public bool IsClean() => _engine.IsClean();

        public void Dispose()
        {
            _server.Dispose();
            _engine.Dispose();
        }
    }
}