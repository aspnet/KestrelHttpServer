// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Server.Abstractions;
using Microsoft.AspNetCore.Server.Infrastructure;

namespace Microsoft.AspNetCore.Server.Filter
{
    public class FilteredStreamAdapter : IDisposable
    {
        private readonly string _connectionId;
        private readonly Stream _filteredStream;
        private readonly IConnectionTrace _log;
        private bool _aborted = false;

        public FilteredStreamAdapter(
            string connectionId,
            Stream filteredStream,
            MemoryPool memory,
            IConnectionTrace logger,
            IThreadPool threadPool)
        {
            SocketInput = new SocketInput(memory, threadPool);
            SocketOutput = new StreamSocketOutput(connectionId, filteredStream, memory, logger);

            _connectionId = connectionId;
            _log = logger;
            _filteredStream = filteredStream;
        }

        public SocketInput SocketInput { get; private set; }

        public ISocketOutput SocketOutput { get; private set; }

        public Task ReadInputAsync()
        {
            return FilterInputAsync();
        }

        public void Abort()
        {
            _aborted = true;
        }

        public void Dispose()
        {
            SocketInput.Dispose();
        }
        
        private async Task FilterInputAsync()
        {
            try
            {
                while (true)
                {
                    // OnAlloc()
                    var block = SocketInput.IncomingStart();

                    int bytesRead = await _filteredStream.ReadAsync(block.Array, block.Data.Offset, block.Data.Count);

                    // OnRead
                    SocketInput.IncomingComplete(bytesRead, null);

                    if (bytesRead == 0)
                    {
                        break;
                    }
                }
                
                if (_aborted)
                {
                    SocketInput.AbortAwaiting();
                }
            }
            catch (TaskCanceledException)
            {
                SocketInput.AbortAwaiting();
                _log.LogError("FilteredStreamAdapter.CopyToAsync canceled.");
            }
            catch (Exception ex)
            {
                SocketInput.AbortAwaiting();
                _log.LogError(0, ex, "FilteredStreamAdapter.CopyToAsync");
            }
            finally
            {
                try
                {
                    SocketInput.IncomingFin();
                }
                catch (Exception ex)
                {
                    _log.LogError(0, ex, "FilteredStreamAdapter.OnStreamClose");
                }
            }

        }
    }
}
