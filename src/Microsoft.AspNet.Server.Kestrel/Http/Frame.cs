﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// ReSharper disable AccessToModifiedClosure

namespace Microsoft.AspNet.Server.Kestrel.Http
{

    public enum ProduceEndType
    {
        SocketShutdownSend,
        SocketDisconnect,
        ConnectionKeepAlive,
    }

    public class FrameContext : ConnectionContext
    {
        public FrameContext()
        {

        }

        public FrameContext(ConnectionContext context) : base(context)
        {

        }

        public IFrameControl FrameControl { get; set; }
    }

    public interface IFrameControl
    {
        Task ProduceContinueAsync();
        Task WriteAsync(ArraySegment<byte> data);
    }

    public class Frame : FrameContext, IFrameControl
    {
        enum Mode
        {
            StartLine,
            MessageHeader,
            MessageBody,
            Terminated,
        }

        Mode _mode;
        private bool _resultStarted;
        private bool _headersSent;
        private bool _keepAlive;

        /*
        //IDictionary<string, object> _environment;

        CancellationTokenSource _cts = new CancellationTokenSource();
        */

        List<KeyValuePair<Action<object>, object>> _onSendingHeaders;
        object _onSendingHeadersSync = new Object();

        public Frame(ConnectionContext context) : base(context)
        {
            FrameControl = this;
            StatusCode = 200;
            RequestHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            ResponseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        public string Method { get; set; }
        public string RequestUri { get; set; }
        public string Path { get; set; }
        public string QueryString { get; set; }
        public string HttpVersion { get; set; }
        public IDictionary<string, string[]> RequestHeaders { get; set; }
        public MessageBody MessageBody { get; set; }
        public Stream RequestBody { get; set; }

        public int StatusCode { get; set; }
        public string ReasonPhrase { get; set; }
        public IDictionary<string, string[]> ResponseHeaders { get; set; }
        public Stream ResponseBody { get; set; }

        public Stream DuplexStream { get; set; }

        public bool HeadersSent
        {
            get { return _headersSent; }
        }


        /*
        public bool LocalIntakeFin
        {
            get
            {
                return _mode == Mode.MessageBody
                    ? _messageBody.LocalIntakeFin
                    : _mode == Mode.Terminated;
            }
        }
        */
        public void Consume()
        {
            var input = SocketInput;
            for (; ;)
            {
                switch (_mode)
                {
                    case Mode.StartLine:
                        if (input.Buffer.Count == 0 && input.RemoteIntakeFin)
                        {
                            _mode = Mode.Terminated;
                            return;
                        }

                        if (!TakeStartLine(input))
                        {
                            if (input.RemoteIntakeFin)
                            {
                                _mode = Mode.Terminated;
                            }
                            return;
                        }

                        _mode = Mode.MessageHeader;
                        break;

                    case Mode.MessageHeader:
                        if (input.Buffer.Count == 0 && input.RemoteIntakeFin)
                        {
                            _mode = Mode.Terminated;
                            return;
                        }

                        var endOfHeaders = false;
                        while (!endOfHeaders)
                        {
                            if (!TakeMessageHeader(input, out endOfHeaders))
                            {
                                if (input.RemoteIntakeFin)
                                {
                                    _mode = Mode.Terminated;
                                }
                                return;
                            }
                        }

                        //var resumeBody = HandleExpectContinue(callback);
                        _mode = Mode.MessageBody;
                        Execute();
                        break;

                    case Mode.MessageBody:
                        if (MessageBody.LocalIntakeFin)
                        {
                            // NOTE: stop reading and resume on keepalive?
                            return;
                        }
                        MessageBody.Consume();
                        // NOTE: keep looping?
                        return;

                    case Mode.Terminated:
                        return;
                }
            }
        }

        private void Execute()
        {
            MessageBody = MessageBody.For(
                HttpVersion,
                RequestHeaders,
                this);
            _keepAlive = MessageBody.RequestKeepAlive;
            RequestBody = new FrameRequestStream(MessageBody);
            ResponseBody = new FrameResponseStream(this);
            DuplexStream = new FrameDuplexStream(RequestBody, ResponseBody);
            SocketInput.Free();
            Task.Run(ExecuteAsync);
        }

        public void OnSendingHeaders(Action<object> callback, object state)
        {
            lock (_onSendingHeadersSync)
            {
                if (_onSendingHeaders == null)
                {
                    _onSendingHeaders = new List<KeyValuePair<Action<object>, object>>();
                }
                _onSendingHeaders.Add(new KeyValuePair<Action<object>, object>(callback, state));
            }
        }

        private void FireOnSendingHeaders()
        {
            List<KeyValuePair<Action<object>, object>> onSendingHeaders = null;
            lock (_onSendingHeadersSync)
            {
                onSendingHeaders = _onSendingHeaders;
                _onSendingHeaders = null;
            }
            if (onSendingHeaders != null)
            {
                foreach (var entry in onSendingHeaders)
                {
                    entry.Key.Invoke(entry.Value);
                }
            }
        }

        private async Task ExecuteAsync()
        {
            Exception error = null;
            try
            {
                await Application.Invoke(this);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                await ProduceEndAsync(error);
            }
        }


        public async Task WriteAsync(ArraySegment<byte> data)
        {
            await ProduceStartAsync();
            await SocketOutput.WriteAsync(data);
        }

        public Task Upgrade(IDictionary<string, object> options, Func<object, Task> callback)
        {
            _keepAlive = false;
            return ProduceStartAsync();

            // NOTE: needs changes
            //_upgradeTask = callback(_callContext);
        }

        byte[] _continueBytes = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");

        public Task ProduceContinueAsync()
        {
            if (_resultStarted)
                return Task.FromResult(0);

            string[] expect;
            if (HttpVersion.Equals("HTTP/1.1") &&
                RequestHeaders.TryGetValue("Expect", out expect) &&
                (expect.FirstOrDefault() ?? "").Equals("100-continue", StringComparison.OrdinalIgnoreCase))
            {
                return SocketOutput.WriteAsync(
                    new ArraySegment<byte>(_continueBytes, 0, _continueBytes.Length));
            }

            return Task.FromResult(0);
        }

        public async Task ProduceStartAsync()
        {
            if (_resultStarted)
                return;

            _resultStarted = true;

            FireOnSendingHeaders();

            _headersSent = true;

            var status = ReasonPhrases.ToStatus(StatusCode, ReasonPhrase);

            var responseHeader = CreateResponseHeader(status, ResponseHeaders);
            try
            {
                await SocketOutput.WriteAsync(responseHeader.Item1);
            }
            finally
            {
                responseHeader.Item2.Dispose();
            }
        }

        public Task ProduceEndAsync(Exception ex)
        {
            var tasks = new List<Task>(3);
            tasks.Add(ProduceStartAsync());

            if (!_keepAlive)
            {
                tasks.Add(ConnectionControl.EndAsync(ProduceEndType.SocketShutdownSend));
            }

            //NOTE: must finish reading request body
            tasks.Add(ConnectionControl.EndAsync(
                _keepAlive ? ProduceEndType.ConnectionKeepAlive : ProduceEndType.SocketDisconnect));

            return Task.WhenAll(tasks);
        }

        private Tuple<ArraySegment<byte>, IDisposable> CreateResponseHeader(
            string status, IEnumerable<KeyValuePair<string, string[]>> headers)
        {
            var writer = new MemoryPoolTextWriter(Memory);
            writer.Write(HttpVersion);
            writer.Write(' ');
            writer.Write(status);
            writer.Write('\r');
            writer.Write('\n');

            var hasConnection = false;
            var hasTransferEncoding = false;
            var hasContentLength = false;
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    var isConnection = false;
                    if (!hasConnection &&
                        string.Equals(header.Key, "Connection", StringComparison.OrdinalIgnoreCase))
                    {
                        hasConnection = isConnection = true;
                    }
                    else if (!hasTransferEncoding &&
                        string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTransferEncoding = true;
                    }
                    else if (!hasContentLength &&
                        string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        hasContentLength = true;
                    }

                    foreach (var value in header.Value)
                    {
                        writer.Write(header.Key);
                        writer.Write(':');
                        writer.Write(' ');
                        writer.Write(value);
                        writer.Write('\r');
                        writer.Write('\n');

                        if (isConnection && value.IndexOf("close", StringComparison.OrdinalIgnoreCase) != -1)
                        {
                            _keepAlive = false;
                        }
                    }
                }
            }

            if (hasTransferEncoding == false && hasContentLength == false)
            {
                _keepAlive = false;
            }
            if (_keepAlive == false && hasConnection == false && HttpVersion == "HTTP/1.1")
            {
                writer.Write("Connection: close\r\n\r\n");
            }
            else if (_keepAlive && hasConnection == false && HttpVersion == "HTTP/1.0")
            {
                writer.Write("Connection: keep-alive\r\n\r\n");
            }
            else
            {
                writer.Write('\r');
                writer.Write('\n');
            }
            writer.Flush();
            return new Tuple<ArraySegment<byte>, IDisposable>(writer.Buffer, writer);
        }

        private bool TakeStartLine(SocketInput baton)
        {
            var remaining = baton.Buffer;
            if (remaining.Count < 2)
            {
                return false;
            }
            var firstSpace = -1;
            var secondSpace = -1;
            var questionMark = -1;
            var ch0 = remaining.Array[remaining.Offset];
            for (var index = 0; index != remaining.Count - 1; ++index)
            {
                var ch1 = remaining.Array[remaining.Offset + index + 1];
                if (ch0 == '\r' && ch1 == '\n')
                {
                    if (secondSpace == -1)
                    {
                        throw new InvalidOperationException("INVALID REQUEST FORMAT");
                    }
                    Method = GetString(remaining, 0, firstSpace);
                    RequestUri = GetString(remaining, firstSpace + 1, secondSpace);
                    if (questionMark == -1)
                    {
                        Path = RequestUri;
                        QueryString = string.Empty;
                    }
                    else
                    {
                        Path = GetString(remaining, firstSpace + 1, questionMark);
                        QueryString = GetString(remaining, questionMark, secondSpace);
                    }
                    HttpVersion = GetString(remaining, secondSpace + 1, index);
                    baton.Skip(index + 2);
                    return true;
                }

                if (ch0 == ' ' && firstSpace == -1)
                {
                    firstSpace = index;
                }
                else if (ch0 == ' ' && firstSpace != -1 && secondSpace == -1)
                {
                    secondSpace = index;
                }
                else if (ch0 == '?' && firstSpace != -1 && questionMark == -1 && secondSpace == -1)
                {
                    questionMark = index;
                }
                ch0 = ch1;
            }
            return false;
        }

        static string GetString(ArraySegment<byte> range, int startIndex, int endIndex)
        {
            return Encoding.UTF8.GetString(range.Array, range.Offset + startIndex, endIndex - startIndex);
        }


        private bool TakeMessageHeader(SocketInput baton, out bool endOfHeaders)
        {
            var remaining = baton.Buffer;
            endOfHeaders = false;
            if (remaining.Count < 2)
            {
                return false;
            }
            var ch0 = remaining.Array[remaining.Offset];
            var ch1 = remaining.Array[remaining.Offset + 1];
            if (ch0 == '\r' && ch1 == '\n')
            {
                endOfHeaders = true;
                baton.Skip(2);
                return true;
            }

            if (remaining.Count < 3)
            {
                return false;
            }
            var wrappedHeaders = false;
            var colonIndex = -1;
            var valueStartIndex = -1;
            var valueEndIndex = -1;
            for (var index = 0; index != remaining.Count - 2; ++index)
            {
                var ch2 = remaining.Array[remaining.Offset + index + 2];
                if (ch0 == '\r' &&
                    ch1 == '\n' &&
                        ch2 != ' ' &&
                            ch2 != '\t')
                {
                    var name = Encoding.ASCII.GetString(remaining.Array, remaining.Offset, colonIndex);
                    var value = "";
                    if (valueEndIndex != -1)
                    {
                        value = Encoding.ASCII.GetString(
                            remaining.Array, remaining.Offset + valueStartIndex, valueEndIndex - valueStartIndex);
                    }
                    if (wrappedHeaders)
                    {
                        value = value.Replace("\r\n", " ");
                    }
                    AddRequestHeader(name, value);
                    baton.Skip(index + 2);
                    return true;
                }
                if (colonIndex == -1 && ch0 == ':')
                {
                    colonIndex = index;
                }
                else if (colonIndex != -1 &&
                    ch0 != ' ' &&
                        ch0 != '\t' &&
                            ch0 != '\r' &&
                                ch0 != '\n')
                {
                    if (valueStartIndex == -1)
                    {
                        valueStartIndex = index;
                    }
                    valueEndIndex = index + 1;
                }
                else if (!wrappedHeaders &&
                    ch0 == '\r' &&
                        ch1 == '\n' &&
                            (ch2 == ' ' ||
                                ch2 == '\t'))
                {
                    wrappedHeaders = true;
                }

                ch0 = ch1;
                ch1 = ch2;
            }
            return false;
        }

        private void AddRequestHeader(string name, string value)
        {
            string[] existing;
            if (!RequestHeaders.TryGetValue(name, out existing) ||
                existing == null ||
                existing.Length == 0)
            {
                RequestHeaders[name] = new[] { value };
            }
            else
            {
                RequestHeaders[name] = existing.Concat(new[] { value }).ToArray();
            }
        }
    }
}
