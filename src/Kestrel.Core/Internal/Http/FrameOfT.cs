// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public class Frame<TContext> : Http1Frame
    {
        private readonly IHttpApplication<TContext> _application;

        private TContext _httpContext;

        public Frame(IHttpApplication<TContext> application, FrameContext context)
            : base(context)
        {
            _application = application;
        }

        protected override void CreateHttpContext() => _httpContext = _application.CreateContext(this);

        protected override void DisposeHttpContext() => _application.DisposeContext(_httpContext, _applicationException);

        protected override Task InvokeApplicationAsync() => _application.ProcessRequestAsync(_httpContext);
    }
}
