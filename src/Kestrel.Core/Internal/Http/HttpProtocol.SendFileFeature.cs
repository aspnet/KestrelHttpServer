// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public partial class HttpProtocol : IHttpSendFileFeature
    {
        async Task IHttpSendFileFeature.SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken)
        {
            var contentLength = ResponseHeaders.ContentLength;
            if (contentLength.HasValue && count.HasValue && contentLength.Value < _responseBytesWritten + count.Value)
            {
                // throw
            }

            using (var fileStream = new FileStream(path,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.ReadWrite,
                                 bufferSize: 1, // Don't create internal buffer
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (offset > 0)
                {
                    fileStream.Seek(offset, SeekOrigin.Begin);
                }

                // Write the filestream directly into the output
                await Output.WriteAsync(fileStream, count ?? long.MaxValue, cancellationToken);
            }
        }
    }
}
