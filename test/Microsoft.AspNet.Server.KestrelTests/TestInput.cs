﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel;
using Microsoft.AspNet.Server.Kestrel.Http;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNet.Server.KestrelTests
{
    class TestInput : IConnectionControl, IFrameControl
    {
        public TestInput()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            var memory2 = new MemoryPool2();
            FrameContext = new FrameContext
            {
                SocketInput = new SocketInput(memory2, ltp),
                ConnectionControl = this,
                FrameControl = this
            };
        }

        public FrameContext FrameContext { get; set; }

        public void Add(string text, bool fin = false)
        {
            var data = Encoding.ASCII.GetBytes(text);
            FrameContext.SocketInput.IncomingData(data, 0, data.Length);

            if (fin)
            {
                FrameContext.SocketInput.RemoteIntakeFin = true;
            }
        }

        public void ProduceContinue()
        {
        }

        public void Pause()
        {
        }

        public void Resume()
        {
        }

        public void Abort()
        {
        }

        public void Write(ArraySegment<byte> data, Action<Exception, object> callback, object state)
        {
        }
        public void End(ProduceEndType endType)
        {
        }

        void IFrameControl.ProduceContinue()
        {
        }

        void IFrameControl.Write(ArraySegment<byte> data)
        {
        }

        Task IFrameControl.WriteAsync(ArraySegment<byte> data, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        void IFrameControl.Flush()
        {
        }

        Task IFrameControl.FlushAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}

