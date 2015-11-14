// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Http.Features;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

// ReSharper disable AccessToModifiedClosure

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public partial class Frame : FrameContext, IFrameControl
    {
        private static readonly Encoding _ascii = Encoding.ASCII;
        private static readonly ArraySegment<byte> _endChunkBytes = CreateAsciiByteArraySegment("\r\n");
        private static readonly ArraySegment<byte> _endChunkedResponseBytes = CreateAsciiByteArraySegment("0\r\n\r\n");
        private static readonly ArraySegment<byte> _continueBytes = CreateAsciiByteArraySegment("HTTP/1.1 100 Continue\r\n\r\n");
        private static readonly ArraySegment<byte> _emptyData = new ArraySegment<byte>(new byte[0]);
        private static readonly byte[] _hex = Encoding.ASCII.GetBytes("0123456789abcdef");

        private static readonly byte[] _bytesEndLine = Encoding.ASCII.GetBytes("\r\n");
        private static readonly byte[] _bytesConnectionClose = Encoding.ASCII.GetBytes("Connection: close\r\n\r\n");
        private static readonly byte[] _bytesConnectionKeepAlive = Encoding.ASCII.GetBytes("Connection: keep-alive\r\n\r\n");
        private static readonly byte[] _bytesTransferEncodingChunked = Encoding.ASCII.GetBytes("Transfer-Encoding: chunked\r\n");
        private static readonly byte[] _bytesHttpVersion1_0 = Encoding.ASCII.GetBytes("HTTP/1.0 ");
        private static readonly byte[] _bytesHttpVersion1_1 = Encoding.ASCII.GetBytes("HTTP/1.1 ");
        private static readonly byte[] _bytesContentLengthZero = Encoding.ASCII.GetBytes("Content-Length: 0\r\n");
        private static readonly byte[] _bytesSpace = Encoding.ASCII.GetBytes(" ");
        private static readonly byte[] _bytesServer = Encoding.ASCII.GetBytes("Server: Kestrel\r\n");
        private static readonly byte[] _bytesDate = Encoding.ASCII.GetBytes("Date: ");

        private readonly object _onStartingSync = new Object();
        private readonly object _onCompletedSync = new Object();
        private readonly FrameRequestHeaders _requestHeaders = new FrameRequestHeaders();
        private readonly FrameResponseHeaders _responseHeaders = new FrameResponseHeaders();

        private List<KeyValuePair<Func<object, Task>, object>> _onStarting;

        private List<KeyValuePair<Func<object, Task>, object>> _onCompleted;

        private bool _requestProcessingStarted;
        private Task _requestProcessingTask;
        private volatile bool _requestProcessingStopping; // volatile, see: https://msdn.microsoft.com/en-us/library/x13ttww7.aspx

        private bool _responseStarted;
        private bool _keepAlive;
        private bool _autoChunk;
        private Exception _applicationException;
        
        private HttpVersionType _httpVersion;

        private readonly IPEndPoint _localEndPoint;
        private readonly IPEndPoint _remoteEndPoint;

        public Frame(ConnectionContext context)
            : this(context, remoteEndPoint: null, localEndPoint: null)
        {
        }

        public Frame(ConnectionContext context,
                     IPEndPoint remoteEndPoint,
                     IPEndPoint localEndPoint)
            : base(context)
        {
            _remoteEndPoint = remoteEndPoint;
            _localEndPoint = localEndPoint;

            FrameControl = this;
            Reset();
        }

        public string Scheme { get; set; }
        public string Method { get; set; }
        public string RequestUri { get; set; }
        public string Path { get; set; }
        public string QueryString { get; set; }
        public string HttpVersion
        {
            get
            {
                if (_httpVersion == HttpVersionType.Http1_1)
                {
                    return "HTTP/1.1";
                }
                if (_httpVersion == HttpVersionType.Http1_0)
                {
                    return "HTTP/1.0";
                }
                return "";
            }
            set
            {
                if (value == "HTTP/1.1")
                {
                    _httpVersion = HttpVersionType.Http1_1;
                }
                else if (value == "HTTP/1.0")
                {
                    _httpVersion = HttpVersionType.Http1_0;
                }
                else
                {
                    _httpVersion = HttpVersionType.Unknown;
                }
            }
        }

        public IHeaderDictionary RequestHeaders { get; set; }
        public MessageBody MessageBody { get; set; }
        public Stream RequestBody { get; set; }

        public int StatusCode { get; set; }
        public string ReasonPhrase { get; set; }
        public IHeaderDictionary ResponseHeaders { get; set; }
        public Stream ResponseBody { get; set; }

        public Stream DuplexStream { get; set; }

        public bool HasResponseStarted
        {
            get { return _responseStarted; }
        }

        public void Reset()
        {
            _onStarting = null;
            _onCompleted = null;

            _responseStarted = false;
            _keepAlive = false;
            _autoChunk = false;
            _applicationException = null;

            _requestHeaders.Reset();
            ResetResponseHeaders();
            ResetFeatureCollection();

            Scheme = null;
            Method = null;
            RequestUri = null;
            Path = null;
            QueryString = null;
            _httpVersion = HttpVersionType.Unknown;
            RequestHeaders = _requestHeaders;
            MessageBody = null;
            RequestBody = null;
            StatusCode = 200;
            ReasonPhrase = null;
            ResponseHeaders = _responseHeaders;
            ResponseBody = null;
            DuplexStream = null;

            var httpConnectionFeature = this as IHttpConnectionFeature;
            httpConnectionFeature.RemoteIpAddress = _remoteEndPoint?.Address;
            httpConnectionFeature.RemotePort = _remoteEndPoint?.Port ?? 0;

            httpConnectionFeature.LocalIpAddress = _localEndPoint?.Address;
            httpConnectionFeature.LocalPort = _localEndPoint?.Port ?? 0;

            if (_remoteEndPoint != null && _localEndPoint != null)
            {
                httpConnectionFeature.IsLocal = _remoteEndPoint.Address.Equals(_localEndPoint.Address);
            }
            else
            {
                httpConnectionFeature.IsLocal = false;
            }
        }

        public void ResetResponseHeaders()
        {
            _responseHeaders.Reset();
            _responseHeaders.HasDefaultDate = true; 
            _responseHeaders.HasDefaultServer = true;
        }

        /// <summary>
        /// Called once by Connection class to begin the RequestProcessingAsync loop.
        /// </summary>
        public void Start()
        {
            if (!_requestProcessingStarted)
            {
                _requestProcessingStarted = true;
                _requestProcessingTask = Task.Run(RequestProcessingAsync);
            }
        }

        /// <summary>
        /// Should be called when the server wants to initiate a shutdown. The Task returned will
        /// become complete when the RequestProcessingAsync function has exited. It is expected that
        /// Stop will be called on all active connections, and Task.WaitAll() will be called on every
        /// return value.
        /// </summary>
        public Task Stop()
        {
            if (!_requestProcessingStopping)
            {
                _requestProcessingStopping = true;
            }
            return _requestProcessingTask ?? TaskUtilities.CompletedTask;
        }

        /// <summary>
        /// Primary loop which consumes socket input, parses it for protocol framing, and invokes the
        /// application delegate for as long as the socket is intended to remain open.
        /// The resulting Task from this loop is preserved in a field which is used when the server needs
        /// to drain and close all currently active connections.
        /// </summary>
        public async Task RequestProcessingAsync()
        {
            try
            {
                var terminated = false;
                while (!terminated && !_requestProcessingStopping)
                {
                    while (!terminated && !_requestProcessingStopping && !TakeStartLine(SocketInput))
                    {
                        terminated = SocketInput.RemoteIntakeFin;
                        if (!terminated)
                        {
                            await SocketInput;
                        }
                    }

                    while (!terminated && !_requestProcessingStopping && !TakeMessageHeaders(SocketInput, _requestHeaders, Memory2))
                    {
                        terminated = SocketInput.RemoteIntakeFin;
                        if (!terminated)
                        {
                            await SocketInput;
                        }
                    }

                    if (!terminated && !_requestProcessingStopping)
                    {
                        MessageBody = MessageBody.For(HttpVersion, _requestHeaders, this);
                        _keepAlive = MessageBody.RequestKeepAlive;
                        RequestBody = new FrameRequestStream(MessageBody);
                        ResponseBody = new FrameResponseStream(this);
                        DuplexStream = new FrameDuplexStream(RequestBody, ResponseBody);

                        var httpContext = HttpContextFactory.Create(this);
                        try
                        {
                            await Application.Invoke(httpContext).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            ReportApplicationError(ex);
                        }
                        finally
                        {
                            // Trigger OnStarting if it hasn't been called yet and the app hasn't
                            // already failed. If an OnStarting callback throws we can go through
                            // our normal error handling in ProduceEnd.
                            // https://github.com/aspnet/KestrelHttpServer/issues/43
                            if (!_responseStarted && _applicationException == null)
                            {
                                await FireOnStarting();
                            }

                            await FireOnCompleted();

                            HttpContextFactory.Dispose(httpContext);

                            await ProduceEnd();
                            
                            while (await MessageBody.SkipAsync() != 0)
                            {
                                // Finish reading the request body in case the app did not.
                            }
                        }

                        terminated = !_keepAlive;
                    }

                    Reset();
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning("Connection processing ended abnormally", ex);
            }
            finally
            {
                try
                {
                    // Inform client no more data will ever arrive
                    ConnectionControl.End(ProduceEndType.SocketShutdownSend);

                    // Wait for client to either disconnect or send unexpected data
                    await SocketInput;

                    // Dispose socket
                    ConnectionControl.End(ProduceEndType.SocketDisconnect);
                }
                catch (Exception ex)
                {
                    Log.LogWarning("Connection shutdown abnormally", ex);
                }
            }
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
            lock (_onStartingSync)
            {
                if (_onStarting == null)
                {
                    _onStarting = new List<KeyValuePair<Func<object, Task>, object>>();
                }
                _onStarting.Add(new KeyValuePair<Func<object, Task>, object>(callback, state));
            }
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
            lock (_onCompletedSync)
            {
                if (_onCompleted == null)
                {
                    _onCompleted = new List<KeyValuePair<Func<object, Task>, object>>();
                }
                _onCompleted.Add(new KeyValuePair<Func<object, Task>, object>(callback, state));
            }
        }

        private async Task FireOnStarting()
        {
            List<KeyValuePair<Func<object, Task>, object>> onStarting = null;
            lock (_onStartingSync)
            {
                onStarting = _onStarting;
                _onStarting = null;
            }
            if (onStarting != null)
            {
                try
                {
                    foreach (var entry in onStarting)
                    {
                        await entry.Key.Invoke(entry.Value);
                    }
                }
                catch (Exception ex)
                {
                    ReportApplicationError(ex);
                }
            }
        }

        private async Task FireOnCompleted()
        {
            List<KeyValuePair<Func<object, Task>, object>> onCompleted = null;
            lock (_onCompletedSync)
            {
                onCompleted = _onCompleted;
                _onCompleted = null;
            }
            if (onCompleted != null)
            {
                foreach (var entry in onCompleted)
                {
                    try
                    {
                        await entry.Key.Invoke(entry.Value);
                    }
                    catch (Exception ex)
                    {
                        ReportApplicationError(ex);
                    }
                }
            }
        }

        public void Flush()
        {
            ProduceStartAndFireOnStarting(immediate: false).GetAwaiter().GetResult();
            SocketOutput.Write(_emptyData, immediate: true);
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            await ProduceStartAndFireOnStarting(immediate: false);
            await SocketOutput.WriteAsync(_emptyData, immediate: true, cancellationToken: cancellationToken);
        }

        public void Write(ArraySegment<byte> data)
        {
            ProduceStartAndFireOnStarting(immediate: false).GetAwaiter().GetResult();

            if (_autoChunk)
            {
                if (data.Count == 0)
                {
                    return;
                }
                WriteChunked(data);
            }
            else
            {
                SocketOutput.Write(data, immediate: true);
            }
        }

        public async Task WriteAsync(ArraySegment<byte> data, CancellationToken cancellationToken)
        {
            await ProduceStartAndFireOnStarting(immediate: false);

            if (_autoChunk)
            {
                if (data.Count == 0)
                {
                    return;
                }
                await WriteChunkedAsync(data, cancellationToken);
            }
            else
            {
                await SocketOutput.WriteAsync(data, immediate: true, cancellationToken: cancellationToken);
            }
        }

        private void WriteChunked(ArraySegment<byte> data)
        {
            SocketOutput.Write(BeginChunkBytes(data.Count), immediate: false);
            SocketOutput.Write(data, immediate: false);
            SocketOutput.Write(_endChunkBytes, immediate: true);
        }

        private async Task WriteChunkedAsync(ArraySegment<byte> data, CancellationToken cancellationToken)
        {
            await SocketOutput.WriteAsync(BeginChunkBytes(data.Count), immediate: false, cancellationToken: cancellationToken);
            await SocketOutput.WriteAsync(data, immediate: false, cancellationToken: cancellationToken);
            await SocketOutput.WriteAsync(_endChunkBytes, immediate: true, cancellationToken: cancellationToken);
        }

        public static ArraySegment<byte> BeginChunkBytes(int dataCount)
        {
            var bytes = new byte[10]
            {
                _hex[((dataCount >> 0x1c) & 0x0f)],
                _hex[((dataCount >> 0x18) & 0x0f)],
                _hex[((dataCount >> 0x14) & 0x0f)],
                _hex[((dataCount >> 0x10) & 0x0f)],
                _hex[((dataCount >> 0x0c) & 0x0f)],
                _hex[((dataCount >> 0x08) & 0x0f)],
                _hex[((dataCount >> 0x04) & 0x0f)],
                _hex[((dataCount >> 0x00) & 0x0f)],
                (byte)'\r',
                (byte)'\n',
            };

            // Determine the most-significant non-zero nibble
            int total, shift;
            total = (dataCount > 0xffff) ? 0x10 : 0x00;
            dataCount >>= total;
            shift = (dataCount > 0x00ff) ? 0x08 : 0x00;
            dataCount >>= shift;
            total |= shift;
            total |= (dataCount > 0x000f) ? 0x04 : 0x00;

            var offset = 7 - (total >> 2);
            return new ArraySegment<byte>(bytes, offset, 10 - offset);
        }

        private void WriteChunkedResponseSuffix()
        {
            SocketOutput.Write(_endChunkedResponseBytes, immediate: true);
        }

        private static ArraySegment<byte> CreateAsciiByteArraySegment(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            return new ArraySegment<byte>(bytes);
        }

        public void ProduceContinue()
        {
            if (_responseStarted) return;

            StringValues expect;
            if (_httpVersion == HttpVersionType.Http1_1 &&
                RequestHeaders.TryGetValue("Expect", out expect) &&
                (expect.FirstOrDefault() ?? "").Equals("100-continue", StringComparison.OrdinalIgnoreCase))
            {
                SocketOutput.Write(_continueBytes);
            }
        }

        public async Task ProduceStartAndFireOnStarting(bool immediate = true)
        {
            if (_responseStarted) return;

            await FireOnStarting();

            if (_applicationException != null)
            {
                throw new ObjectDisposedException(
                    "The response has been aborted due to an unhandled application exception.",
                    _applicationException);
            }

            await ProduceStart(immediate, appCompleted: false);
        }

        private Task ProduceStart(bool immediate, bool appCompleted)
        {
            if (_responseStarted) return TaskUtilities.CompletedTask;
            _responseStarted = true;

            var statusBytes = ReasonPhrases.ToStatusBytes(StatusCode, ReasonPhrase);

            return CreateResponseHeader(statusBytes, appCompleted, immediate);
        }

        private async Task ProduceEnd()
        {
            if (_applicationException != null)
            {
                if (_responseStarted)
                {
                    // We can no longer respond with a 500, so we simply close the connection.
                    _requestProcessingStopping = true;
                    return;
                }
                else
                {
                    StatusCode = 500;
                    ReasonPhrase = null;

                    ResetResponseHeaders();
                    _responseHeaders.HeaderContentLength = "0";
                }
            }

            await ProduceStart(immediate: true, appCompleted: true);

            // _autoChunk should be checked after we are sure ProduceStart() has been called
            // since ProduceStart() may set _autoChunk to true.
            if (_autoChunk)
            {
                WriteChunkedResponseSuffix();
            }

            if (_keepAlive)
            {
                ConnectionControl.End(ProduceEndType.ConnectionKeepAlive);
            }
        }

        private static void OutputAsciiBlock(string data, MemoryPoolBlock2 memoryBlock, ISocketOutput output)
        {
            var start = memoryBlock.Start;
            var end = start + memoryBlock.Data.Count;
            var array = memoryBlock.Array;
            var current = memoryBlock.End;

            foreach (var chr in data)
            {
                array[current] = (byte)chr;

                current++;

                if (current == end)
                {
                    output.Write(memoryBlock.Data, immediate: false);
                    current = start;
                }
            }
            memoryBlock.End = current;
        }

        private static void OutputAsciiBlock(byte[] data, MemoryPoolBlock2 memoryBlock, ISocketOutput output)
        {
            var offset = 0;
            var remaining = data.Length;

            var start = memoryBlock.Start;
            var end = start + memoryBlock.Data.Count;
            var array = memoryBlock.Array;
            var current = memoryBlock.End;

            while (remaining > 0)
            {
                var blockRemaining = end - current;
                var copyAmount = blockRemaining >= remaining ? remaining : blockRemaining;
                Buffer.BlockCopy(data, offset, array, current, copyAmount);

                current += copyAmount;
                remaining -= copyAmount;
                offset += copyAmount;

                if (current == end)
                {
                    output.Write(memoryBlock.Data, immediate: false);
                    current = start;
                }
            }
            memoryBlock.End = current;
        }

        private Task CreateResponseHeader(
            byte[] statusBytes,
            bool appCompleted,
            bool immediate)
        {
            var memoryBlock = Memory2.Lease();
            try
            {
                var blockRemaining = memoryBlock.Data.Count;

                OutputAsciiBlock(_httpVersion == HttpVersionType.Http1_1 ? _bytesHttpVersion1_1 : _bytesHttpVersion1_0, memoryBlock, SocketOutput);

                OutputAsciiBlock(statusBytes, memoryBlock, SocketOutput);

                if (_responseHeaders.HasDefaultDate)
                {
                    OutputAsciiBlock(_bytesDate, memoryBlock, SocketOutput);
                    OutputAsciiBlock(DateHeaderValueManager.GetDateHeaderValueBytes(), memoryBlock, SocketOutput);
                    OutputAsciiBlock(_bytesEndLine, memoryBlock, SocketOutput);
                }

                foreach (var header in _responseHeaders.AsOutputEnumerable())
                {
                    foreach (var value in header.Value)
                    {
                        OutputAsciiBlock(header.Key, memoryBlock, SocketOutput);
                        OutputAsciiBlock(value, memoryBlock, SocketOutput);
                        OutputAsciiBlock(_bytesEndLine, memoryBlock, SocketOutput);

                        if (_responseHeaders.HasConnection && value.IndexOf("close", StringComparison.OrdinalIgnoreCase) != -1)
                        {
                            _keepAlive = false;
                        }
                    }
                }

                if (_responseHeaders.HasDefaultServer)
                {
                    OutputAsciiBlock(_bytesServer, memoryBlock, SocketOutput);
                }

                if (_keepAlive && !_responseHeaders.HasTransferEncoding && !_responseHeaders.HasContentLength)
                {
                    if (appCompleted)
                    {
                        // Don't set the Content-Length or Transfer-Encoding headers
                        // automatically for HEAD requests or 101, 204, 205, 304 responses.
                        if (Method != "HEAD" && StatusCanHaveBody(StatusCode))
                        {
                            // Since the app has completed and we are only now generating
                            // the headers we can safely set the Content-Length to 0.
                            OutputAsciiBlock(_bytesContentLengthZero, memoryBlock, SocketOutput);
                        }
                    }
                    else
                    {
                        if (_httpVersion == HttpVersionType.Http1_1)
                        {
                            _autoChunk = true;
                            OutputAsciiBlock(_bytesTransferEncodingChunked, memoryBlock, SocketOutput);
                        }
                        else
                        {
                            _keepAlive = false;
                        }
                    }
                }

                if (_keepAlive == false && _responseHeaders.HasConnection == false && _httpVersion == HttpVersionType.Http1_1)
                {
                    OutputAsciiBlock(_bytesConnectionClose, memoryBlock, SocketOutput);
                }
                else if (_keepAlive && _responseHeaders.HasConnection == false && _httpVersion == HttpVersionType.Http1_0)
                {
                    OutputAsciiBlock(_bytesConnectionKeepAlive, memoryBlock, SocketOutput);
                }
                else
                {
                    OutputAsciiBlock(_bytesEndLine, memoryBlock, SocketOutput);
                }

                return SocketOutput.WriteAsync(
                    (memoryBlock.Start == memoryBlock.End) ?
                    default(ArraySegment<byte>) :
                    new ArraySegment<byte>(memoryBlock.Array, memoryBlock.Start, memoryBlock.End - memoryBlock.Start),
                    immediate);
            }
            finally
            {
                Memory2.Return(memoryBlock);
            }
        }

        private bool TakeStartLine(SocketInput input)
        {
            var scan = input.ConsumingStart();
            var consumed = scan;
            try
            {
                var begin = scan;
                if (scan.Seek(' ') == -1)
                {
                    return false;
                }
                var method = begin.GetAsciiString(ref scan);

                scan.Take();
                begin = scan;

                var needDecode = false;
                var chFound = scan.Seek(' ', '?', '%');
                if (chFound == '%')
                {
                    needDecode = true;
                    chFound = scan.Seek(' ', '?');
                }

                var pathBegin = begin;
                var pathEnd = scan;

                var queryString = "";
                if (chFound == '?')
                {
                    begin = scan;
                    if (scan.Seek(' ') != ' ')
                    {
                        return false;
                    }
                    queryString = begin.GetAsciiString(ref scan);
                }

                scan.Take();
                begin = scan;
                if (scan.Seek('\r') == -1)
                {
                    return false;
                }
                var httpVersion = begin.GetAsciiString(ref scan);

                scan.Take();
                if (scan.Take() != '\n')
                {
                    return false;
                }

                string requestUrlPath;
                if (needDecode)
                {
                    pathEnd = UrlPathDecoder.Unescape(pathBegin, pathEnd);
                    requestUrlPath = pathBegin.GetUtf8String(ref pathEnd);
                }
                else
                {
                    requestUrlPath = pathBegin.GetAsciiString(ref pathEnd);
                }

                consumed = scan;
                Method = method;
                RequestUri = requestUrlPath;
                QueryString = queryString;
                HttpVersion = httpVersion;
                Path = RequestUri;
                return true;
            }
            finally
            {
                input.ConsumingComplete(consumed, scan);
            }
        }

        public static bool TakeMessageHeaders(SocketInput input, FrameRequestHeaders requestHeaders, MemoryPool2 memorypool)
        {
            var scan = input.ConsumingStart();
            var consumed = scan;
            try
            {
                int chFirst;
                int chSecond;
                while (!scan.IsEnd)
                {
                    var beginName = scan;
                    scan.Seek(':', '\r');
                    var endName = scan;

                    chFirst = scan.Take();
                    var beginValue = scan;
                    chSecond = scan.Take();

                    if (chFirst == -1 || chSecond == -1)
                    {
                        return false;
                    }
                    if (chFirst == '\r')
                    {
                        if (chSecond == '\n')
                        {
                            consumed = scan;
                            return true;
                        }
                        throw new InvalidDataException("Malformed request");
                    }

                    while (
                        chSecond == ' ' ||
                        chSecond == '\t' ||
                        chSecond == '\r' ||
                        chSecond == '\n')
                    {
                        if (chSecond == '\r')
                        {
                            var scanAhead = scan;
                            var chAhead = scanAhead.Take();
                            if (chAhead == '\n')
                            {
                                chAhead = scanAhead.Take();
                                // If the "\r\n" isn't part of "linear whitespace",
                                // then this header has no value.
                                if (chAhead != ' ' && chAhead != '\t')
                                {
                                    break;
                                }
                            }
                        }

                        beginValue = scan;
                        chSecond = scan.Take();
                    }
                    scan = beginValue;

                    var wrapping = false;

                    var memoryBlock = memorypool.Lease();
                    try
                    {
                        while (!scan.IsEnd)
                        {
                            if (scan.Seek('\r') == -1)
                            {
                                // no "\r" in sight, burn used bytes and go back to await more data
                                return false;
                            }

                            var endValue = scan;
                            chFirst = scan.Take(); // expecting: /r
                            chSecond = scan.Take(); // expecting: /n

                            if (chSecond != '\n')
                            {
                                // "\r" was all by itself, move just after it and try again
                                scan = endValue;
                                scan.Take();
                                continue;
                            }

                            var chThird = scan.Peek();
                            if (chThird == ' ' || chThird == '\t')
                            {
                                // special case, "\r\n " or "\r\n\t".
                                // this is considered wrapping"linear whitespace" and is actually part of the header value
                                // continue past this for the next
                                wrapping = true;
                                continue;
                            }

                            var name = beginName.GetArraySegment(ref endName, memoryBlock.Data);
                            var value = beginValue.GetAsciiString(ref endValue);
                            if (wrapping)
                            {
                                value = value.Replace("\r\n", " ");
                            }

                            consumed = scan;
                            requestHeaders.Append(name.Array, name.Offset, name.Count, value);
                            break;
                        }
                    }
                    finally
                    {
                        memorypool.Return(memoryBlock);
                    }
                }
                return false;
            }
            finally
            {
                input.ConsumingComplete(consumed, scan);
            }
        }

        public bool StatusCanHaveBody(int statusCode)
        {
            // List of status codes taken from Microsoft.Net.Http.Server.Response
            return statusCode != 101 &&
                   statusCode != 204 &&
                   statusCode != 205 &&
                   statusCode != 304;
        }

        private void ReportApplicationError(Exception ex)
        {
            _applicationException = ex;
            Log.ApplicationError(ex);
        }

        private enum HttpVersionType
        {
            Unknown = -1,
            Http1_0 = 0,
            Http1_1 = 1
        }
    }
}
