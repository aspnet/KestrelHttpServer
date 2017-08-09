// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Protocols.Abstractions.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal
{
    public class LoggingConnectionAdapter
    {
        private readonly ConnectionDelegate _next;
        private readonly ILogger _logger;

        public LoggingConnectionAdapter(ConnectionDelegate next, ILogger logger)
        {
            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _next = next;
            _logger = logger;
        }

        public async Task OnConnectionAsync(ConnectionContext context)
        {
            var transportFeature = context.Features.Get<IConnectionTransportFeature>();

            var stream = new LoggingStream(new PipeStream(context.Transport.Reader, context.Transport.Writer), _logger);
            var pipe = new StreamPipe(transportFeature.PipeFactory);

            context.Transport = new StreamPipe(transportFeature.PipeFactory);

            var task = pipe.CopyFromAsync(stream);

            await _next(context);

            await task;
        }
    }
}
