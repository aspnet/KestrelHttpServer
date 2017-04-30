// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class Http2ConnectionTests : IDisposable
    {
        private readonly PipeFactory _pipeFactory;
        private readonly IPipe _inputPipe;
        private readonly IPipe _outputPipe;
        private readonly Http2ConnectionContext _connectionContext;
        private readonly Http2PeerSettings _clientSettings;

        public Http2ConnectionTests()
        {
            _pipeFactory = new PipeFactory();
            _inputPipe = _pipeFactory.Create();
            _outputPipe = _pipeFactory.Create();
            _connectionContext = new Http2ConnectionContext
            {
                ServiceContext = new TestServiceContext(),
                Input = _inputPipe.Reader,
                Output = _outputPipe
            };
            _clientSettings = new Http2PeerSettings();
        }

        public void Dispose()
        {
            _pipeFactory.Dispose();
        }

        [Fact]
        public async Task SendsSettingsAck()
        {
            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication());

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            _inputPipe.Writer.Complete();

            connection.Stop();
            await connectionTask;
        }

        [Fact]
        public async Task SendsPingAck()
        {
            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication());

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            var pingFrame = new Http2Frame();
            pingFrame.PreparePing(Http2PingFrameFlags.NONE);
            await SendAsync(pingFrame.Raw);

            var responseFrame = await ReceiveFrameAsync();

            Assert.Equal(Http2FrameType.PING, responseFrame.Type);
            Assert.Equal(Http2PingFrameFlags.ACK, responseFrame.PingFlags);

            _inputPipe.Writer.Complete();

            connection.Stop();
            await connectionTask;
        }

        private async Task SendAsync(Span<byte> span)
        {
            var writableBuffer = _inputPipe.Writer.Alloc(1);
            writableBuffer.Write(span);
            await writableBuffer.FlushAsync();
        }

        private Task SendPreambleAsync() => SendAsync(Http2Connection.Preface);

        private Task SendClientSettingsAsync()
        {
            var frame = new Http2Frame();
            frame.PrepareSettings(Http2SettingsFrameFlags.NONE, _clientSettings);
            return SendAsync(frame.Raw);
        }

        private async Task<Http2Frame> ReceiveFrameAsync()
        {
            var frame = new Http2Frame();

            while (true)
            {
                var result = await _outputPipe.Reader.ReadAsync();
                var buffer = result.Buffer;
                var consumed = buffer.Start;
                var examined = buffer.End;

                try
                {
                    Assert.True(buffer.Length > 0);

                    if (Http2FrameReader.ReadFrame(buffer, frame, out consumed, out examined))
                    {
                        return frame;
                    }
                }
                finally
                {
                    _outputPipe.Reader.Advance(consumed, examined);
                }
            }
        }

        private async Task ReceiveSettingsAck()
        {
            var frame = await ReceiveFrameAsync();

            Assert.Equal(Http2FrameType.SETTINGS, frame.Type);
            Assert.Equal(Http2SettingsFrameFlags.ACK, frame.SettingsFlags);
        }
    }
}
