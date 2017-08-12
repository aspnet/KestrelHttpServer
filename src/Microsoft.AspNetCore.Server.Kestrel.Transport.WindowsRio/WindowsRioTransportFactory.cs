// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio
{
    public sealed class WindowsRioTransportFactory : ITransportFactory
    {
        private readonly PipeFactory _pipeFactory;
        private readonly BufferMapper _bufferMapper;
        private readonly bool _forceDispatch;

        public BufferMapper BufferMapper => _bufferMapper;

        public WindowsRioTransportFactory(bool forceDispatch = false)
        {
            _forceDispatch = forceDispatch;

            var memoryPool = new MemoryPool();
            _bufferMapper = new BufferMapper(memoryPool);
            _pipeFactory = new PipeFactory(memoryPool);
        }

        public ITransport Create(IEndPointInformation endPointInformation, IConnectionHandler handler)
        {
            if (endPointInformation == null)
            {
                throw new ArgumentNullException(nameof(endPointInformation));
            }

            if (endPointInformation.Type != ListenType.IPEndPoint)
            {
                throw new ArgumentException("Only ListenType.IPEndPoint is supported", nameof(endPointInformation));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return new WindowsRioTransport(this, endPointInformation, handler);
        }

        internal PipeFactory PipeFactory => _pipeFactory;

        internal bool ForceDispatch => _forceDispatch;
    }
}
