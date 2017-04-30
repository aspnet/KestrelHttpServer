// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Tls
{
    public class TlsStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly IAlpnHandler _alpnHandler;

        private IntPtr _ctx;
        private IntPtr _ssl;
        private IntPtr _inputBio;
        private IntPtr _outputBio;

        private readonly SemaphoreSlim _inputAvailable = new SemaphoreSlim(0);

        private readonly byte[] _inputBuffer = new byte[1024 * 1024];
        private readonly byte[] _outputBuffer = new byte[1024 * 1024];

        static TlsStream()
        {
            OpenSsl.SSL_library_init();
            OpenSsl.SSL_load_error_strings();
            OpenSsl.ERR_load_BIO_strings();
            OpenSsl.OpenSSL_add_all_algorithms();
        }

        public TlsStream(Stream innerStream, string certificatePath, string privateKeyPath, IAlpnHandler alpnHandler)
        {
            _innerStream = innerStream;
            _alpnHandler = alpnHandler;

            _ctx = OpenSsl.SSL_CTX_new(OpenSsl.TLSv1_2_method());

            if (_ctx == IntPtr.Zero)
            {
                throw new Exception("Unable to create SSL context.");
            }

            OpenSsl.SSL_CTX_set_ecdh_auto(_ctx, 1);

            if (OpenSsl.SSL_CTX_use_certificate_file(_ctx, certificatePath, 1) != 1)
            {
                throw new Exception("Unable to load certificate file.");
            }

            if (OpenSsl.SSL_CTX_use_PrivateKey_file(_ctx, privateKeyPath, 1) != 1)
            {
                throw new Exception("Unable to load private key file.");
            }

            OpenSsl.SSL_CTX_set_alpn_select_cb(_ctx, _alpnHandler);

            _ssl = OpenSsl.SSL_new(_ctx);

            _inputBio = OpenSsl.BIO_new(OpenSsl.BIO_s_mem());
            OpenSsl.BIO_set_mem_eof_return(_inputBio, -1);

            _outputBio = OpenSsl.BIO_new(OpenSsl.BIO_s_mem());
            OpenSsl.BIO_set_mem_eof_return(_outputBio, -1);

            OpenSsl.SSL_set_bio(_ssl, _inputBio, _outputBio);
        }

        public override bool CanRead => true;
        public override bool CanWrite => true;

        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Flush()
        {
            FlushAsync(default(CancellationToken)).GetAwaiter().GetResult();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            var pending = OpenSsl.BIO_ctrl_pending(_outputBio);

            while (pending > 0)
            {
                var count = OpenSsl.BIO_read(_outputBio, _outputBuffer, 0, _outputBuffer.Length);
                await _innerStream.WriteAsync(_outputBuffer, 0, count, cancellationToken);

                pending = OpenSsl.BIO_ctrl_pending(_outputBio);
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (OpenSsl.BIO_ctrl_pending(_inputBio) == 0)
            {
                var bytesRead = await _innerStream.ReadAsync(_inputBuffer, 0, _inputBuffer.Length, cancellationToken);
                OpenSsl.BIO_write(_inputBio, _inputBuffer, 0, bytesRead);
            }

            return OpenSsl.SSL_read(_ssl, buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            OpenSsl.SSL_write(_ssl, buffer, offset, count);

            return FlushAsync(cancellationToken);
        }

        public async Task DoHandshakeAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            OpenSsl.SSL_set_accept_state(_ssl);

            var count = 0;

            while ((count = await _innerStream.ReadAsync(_inputBuffer, 0, _inputBuffer.Length)) > 0)
            {
                OpenSsl.BIO_write(_inputBio, _inputBuffer, 0, count);
                var ret = OpenSsl.SSL_do_handshake(_ssl);

                if (ret != 1)
                {
                    var error = OpenSsl.SSL_get_error(_ssl,  ret);

                    if (error != 2)
                    {
                        throw new Exception($"{nameof(OpenSsl.SSL_do_handshake)} failed: {error}.");
                    }
                }

                await FlushAsync(cancellationToken);

                if (ret == 1)
                {
                    return;
                }
            }
        }
    }
}
