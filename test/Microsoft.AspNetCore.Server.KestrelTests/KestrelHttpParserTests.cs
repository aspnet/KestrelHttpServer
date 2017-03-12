// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class KestrelHttpParserTests : HttpParserTests<KestrelHttpParser>
    {
        protected override IHttpParser CreateParser(object state) => new KestrelHttpParser((IKestrelTrace)state);
    }
}
