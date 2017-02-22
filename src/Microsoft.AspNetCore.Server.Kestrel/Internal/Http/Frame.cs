// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipelines.Text.Primitives;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web.Utf8;
using System.Text.Utf8;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Adapter;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

// ReSharper disable AccessToModifiedClosure

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public abstract partial class Frame : IFrameControl
    {
        // byte types don't have a data type annotation so we pre-cast them; to avoid in-place casts
        private const byte ByteCR = (byte)'\r';
        private const byte ByteLF = (byte)'\n';
        private const byte ByteColon = (byte)':';
        private const byte ByteSpace = (byte)' ';
        private const byte ByteTab = (byte)'\t';
        private const byte ByteQuestionMark = (byte)'?';
        private const byte BytePercentage = (byte)'%';

        private static readonly ArraySegment<byte> _endChunkedResponseBytes = CreateAsciiByteArraySegment("0\r\n\r\n");
        private static readonly ArraySegment<byte> _continueBytes = CreateAsciiByteArraySegment("HTTP/1.1 100 Continue\r\n\r\n");

        private static readonly byte[] _bytesConnectionClose = Encoding.ASCII.GetBytes("\r\nConnection: close");
        private static readonly byte[] _bytesConnectionKeepAlive = Encoding.ASCII.GetBytes("\r\nConnection: keep-alive");
        private static readonly byte[] _bytesTransferEncodingChunked = Encoding.ASCII.GetBytes("\r\nTransfer-Encoding: chunked");
        private static readonly byte[] _bytesHttpVersion11 = Encoding.ASCII.GetBytes("HTTP/1.1 ");
        private static readonly byte[] _bytesEndHeaders = Encoding.ASCII.GetBytes("\r\n\r\n");
        private static readonly byte[] _bytesServer = Encoding.ASCII.GetBytes("\r\nServer: Kestrel");

        private readonly object _onStartingSync = new Object();
        private readonly object _onCompletedSync = new Object();

        private Streams _frameStreams;

        protected Stack<KeyValuePair<Func<object, Task>, object>> _onStarting;
        protected Stack<KeyValuePair<Func<object, Task>, object>> _onCompleted;

        private TaskCompletionSource<object> _frameStartedTcs = new TaskCompletionSource<object>();
        private Task _requestProcessingTask;
        protected volatile bool _requestProcessingStopping; // volatile, see: https://msdn.microsoft.com/en-us/library/x13ttww7.aspx
        protected int _requestAborted;
        private CancellationTokenSource _abortedCts;
        private CancellationToken? _manuallySetRequestAbortToken;

        private RequestProcessingStatus _requestProcessingStatus;
        protected bool _keepAlive;
        protected bool _upgrade;
        private bool _canHaveBody;
        private bool _autoChunk;
        protected Exception _applicationException;
        private BadHttpRequestException _requestRejectedException;

        protected HttpVersion _httpVersion;

        private readonly string _pathBase;

        private int _remainingRequestHeadersBytesAllowed;
        private int _requestHeadersParsed;

        protected readonly long _keepAliveMilliseconds;
        private readonly long _requestHeadersTimeoutMilliseconds;

        protected long _responseBytesWritten;

        public Frame(ConnectionContext context)
        {
            ConnectionContext = context;
            Input = context.Input;
            Output = context.Output;

            ServerOptions = context.ListenerContext.ServiceContext.ServerOptions;

            _pathBase = context.ListenerContext.ListenOptions.PathBase;

            FrameControl = this;
            _keepAliveMilliseconds = (long)ServerOptions.Limits.KeepAliveTimeout.TotalMilliseconds;
            _requestHeadersTimeoutMilliseconds = (long)ServerOptions.Limits.RequestHeadersTimeout.TotalMilliseconds;
        }

        public ConnectionContext ConnectionContext { get; }
        public IPipe Input { get; set; }
        public ISocketOutput Output { get; set; }
        public IEnumerable<IAdaptedConnection> AdaptedConnections { get; set; }

        protected IConnectionControl ConnectionControl => ConnectionContext.ConnectionControl;
        protected IKestrelTrace Log => ConnectionContext.ListenerContext.ServiceContext.Log;

        private DateHeaderValueManager DateHeaderValueManager => ConnectionContext.ListenerContext.ServiceContext.DateHeaderValueManager;
        // Hold direct reference to ServerOptions since this is used very often in the request processing path
        private KestrelServerOptions ServerOptions { get; }
        private IPEndPoint LocalEndPoint => ConnectionContext.LocalEndPoint;
        private IPEndPoint RemoteEndPoint => ConnectionContext.RemoteEndPoint;
        protected string ConnectionId => ConnectionContext.ConnectionId;

        public string ConnectionIdFeature { get; set; }
        public IPAddress RemoteIpAddress { get; set; }
        public int RemotePort { get; set; }
        public IPAddress LocalIpAddress { get; set; }
        public int LocalPort { get; set; }
        public string Scheme { get; set; }
        public string Method { get; set; }
        public string PathBase { get; set; }
        public string Path { get; set; }
        public string QueryString { get; set; }
        public string RawTarget { get; set; }
        public string HttpVersion
        {
            get
            {
                if (_httpVersion == Http.HttpVersion.Http11)
                {
                    return "HTTP/1.1";
                }
                if (_httpVersion == Http.HttpVersion.Http10)
                {
                    return "HTTP/1.0";
                }

                return string.Empty;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                // GetKnownVersion returns versions which ReferenceEquals interned string
                // As most common path, check for this only in fast-path and inline
                if (ReferenceEquals(value, "HTTP/1.1"))
                {
                    _httpVersion = Http.HttpVersion.Http11;
                }
                else if (ReferenceEquals(value, "HTTP/1.0"))
                {
                    _httpVersion = Http.HttpVersion.Http10;
                }
                else
                {
                    HttpVersionSetSlow(value);
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void HttpVersionSetSlow(string value)
        {
            if (value == "HTTP/1.1")
            {
                _httpVersion = Http.HttpVersion.Http11;
            }
            else if (value == "HTTP/1.0")
            {
                _httpVersion = Http.HttpVersion.Http10;
            }
            else
            {
                _httpVersion = Http.HttpVersion.Unset;
            }
        }

        public IHeaderDictionary RequestHeaders { get; set; }
        public Stream RequestBody { get; set; }

        private int _statusCode;
        public int StatusCode
        {
            get
            {
                return _statusCode;
            }
            set
            {
                if (HasResponseStarted)
                {
                    ThrowResponseAlreadyStartedException(nameof(StatusCode));
                }

                _statusCode = value;
            }
        }

        private string _reasonPhrase;
        public string ReasonPhrase
        {
            get
            {
                return _reasonPhrase;
            }
            set
            {
                if (HasResponseStarted)
                {
                    ThrowResponseAlreadyStartedException(nameof(ReasonPhrase));
                }

                _reasonPhrase = value;
            }
        }

        public IHeaderDictionary ResponseHeaders { get; set; }
        public Stream ResponseBody { get; set; }

        public Stream DuplexStream { get; set; }

        public Task FrameStartedTask => _frameStartedTcs.Task;

        public CancellationToken RequestAborted
        {
            get
            {
                // If a request abort token was previously explicitly set, return it.
                if (_manuallySetRequestAbortToken.HasValue)
                {
                    return _manuallySetRequestAbortToken.Value;
                }
                // Otherwise, get the abort CTS.  If we have one, which would mean that someone previously
                // asked for the RequestAborted token, simply return its token.  If we don't,
                // check to see whether we've already aborted, in which case just return an
                // already canceled token.  Finally, force a source into existence if we still
                // don't have one, and return its token.
                var cts = _abortedCts;
                return
                    cts != null ? cts.Token :
                    (Volatile.Read(ref _requestAborted) == 1) ? new CancellationToken(true) :
                    RequestAbortedSource.Token;
            }
            set
            {
                // Set an abort token, overriding one we create internally.  This setter and associated
                // field exist purely to support IHttpRequestLifetimeFeature.set_RequestAborted.
                _manuallySetRequestAbortToken = value;
            }
        }

        private CancellationTokenSource RequestAbortedSource
        {
            get
            {
                // Get the abort token, lazily-initializing it if necessary.
                // Make sure it's canceled if an abort request already came in.

                // EnsureInitialized can return null since _abortedCts is reset to null
                // after it's already been initialized to a non-null value.
                // If EnsureInitialized does return null, this property was accessed between
                // requests so it's safe to return an ephemeral CancellationTokenSource.
                var cts = LazyInitializer.EnsureInitialized(ref _abortedCts, () => new CancellationTokenSource())
                            ?? new CancellationTokenSource();

                if (Volatile.Read(ref _requestAborted) == 1)
                {
                    cts.Cancel();
                }
                return cts;
            }
        }

        public bool HasResponseStarted => _requestProcessingStatus == RequestProcessingStatus.ResponseStarted;

        protected FrameRequestHeaders FrameRequestHeaders { get; private set; }

        protected FrameResponseHeaders FrameResponseHeaders { get; private set; }

        public void InitializeHeaders()
        {
            if (FrameRequestHeaders == null)
            {
                FrameRequestHeaders = new FrameRequestHeaders();
            }

            RequestHeaders = FrameRequestHeaders;

            if (FrameResponseHeaders == null)
            {
                FrameResponseHeaders = new FrameResponseHeaders();
            }

            ResponseHeaders = FrameResponseHeaders;
        }

        public void InitializeStreams(MessageBody messageBody)
        {
            if (_frameStreams == null)
            {
                _frameStreams = new Streams(this);
            }

            RequestBody = _frameStreams.RequestBody;
            ResponseBody = _frameStreams.ResponseBody;
            DuplexStream = _frameStreams.DuplexStream;

            _frameStreams.RequestBody.StartAcceptingReads(messageBody);
            _frameStreams.ResponseBody.StartAcceptingWrites();
        }

        public void PauseStreams()
        {
            _frameStreams.RequestBody.PauseAcceptingReads();
            _frameStreams.ResponseBody.PauseAcceptingWrites();
        }

        public void ResumeStreams()
        {
            _frameStreams.RequestBody.ResumeAcceptingReads();
            _frameStreams.ResponseBody.ResumeAcceptingWrites();
        }

        public void StopStreams()
        {
            _frameStreams.RequestBody.StopAcceptingReads();
            _frameStreams.ResponseBody.StopAcceptingWrites();
        }

        public void Reset()
        {
            FrameRequestHeaders?.Reset();
            FrameResponseHeaders?.Reset();

            _onStarting = null;
            _onCompleted = null;

            _requestProcessingStatus = RequestProcessingStatus.RequestPending;
            _keepAlive = false;
            _autoChunk = false;
            _applicationException = null;

            ResetFeatureCollection();

            Scheme = null;
            Method = null;
            PathBase = null;
            Path = null;
            QueryString = null;
            _httpVersion = Http.HttpVersion.Unset;
            StatusCode = StatusCodes.Status200OK;
            ReasonPhrase = null;

            RemoteIpAddress = RemoteEndPoint?.Address;
            RemotePort = RemoteEndPoint?.Port ?? 0;

            LocalIpAddress = LocalEndPoint?.Address;
            LocalPort = LocalEndPoint?.Port ?? 0;
            ConnectionIdFeature = ConnectionId;

            if (AdaptedConnections != null)
            {
                try
                {
                    foreach (var adaptedConnection in AdaptedConnections)
                    {
                        adaptedConnection.PrepareRequest(this);
                    }
                }
                catch (Exception ex)
                {
                    Log.LogError(0, ex, $"Uncaught exception from the {nameof(IAdaptedConnection.PrepareRequest)} method of an {nameof(IAdaptedConnection)}.");
                }
            }

            _manuallySetRequestAbortToken = null;
            _abortedCts = null;

            _remainingRequestHeadersBytesAllowed = ServerOptions.Limits.MaxRequestHeadersTotalSize;
            _requestHeadersParsed = 0;

            _responseBytesWritten = 0;
        }

        /// <summary>
        /// Called once by Connection class to begin the RequestProcessingAsync loop.
        /// </summary>
        public void Start()
        {
            Reset();
            _requestProcessingTask = RequestProcessingAsync();
            _frameStartedTcs.SetResult(null);
        }

        /// <summary>
        /// Should be called when the server wants to initiate a shutdown. The Task returned will
        /// become complete when the RequestProcessingAsync function has exited. It is expected that
        /// Stop will be called on all active connections, and Task.WaitAll() will be called on every
        /// return value.
        /// </summary>
        public Task StopAsync()
        {
            if (!_requestProcessingStopping)
            {
                _requestProcessingStopping = true;
            }

            return _requestProcessingTask ?? TaskCache.CompletedTask;
        }

        /// <summary>
        /// Immediate kill the connection and poison the request and response streams.
        /// </summary>
        public void Abort(Exception error = null)
        {
            if (Interlocked.Exchange(ref _requestAborted, 1) == 0)
            {
                _requestProcessingStopping = true;

                _frameStreams?.RequestBody.Abort(error);
                _frameStreams?.ResponseBody.Abort();

                try
                {
                    ConnectionControl.End(ProduceEndType.SocketDisconnect);
                }
                catch (Exception ex)
                {
                    Log.LogError(0, ex, "Abort");
                }

                try
                {
                    RequestAbortedSource.Cancel();
                }
                catch (Exception ex)
                {
                    Log.LogError(0, ex, "Abort");
                }
                _abortedCts = null;
            }
        }

        /// <summary>
        /// Primary loop which consumes socket input, parses it for protocol framing, and invokes the
        /// application delegate for as long as the socket is intended to remain open.
        /// The resulting Task from this loop is preserved in a field which is used when the server needs
        /// to drain and close all currently active connections.
        /// </summary>
        public abstract Task RequestProcessingAsync();

        public void OnStarting(Func<object, Task> callback, object state)
        {
            lock (_onStartingSync)
            {
                if (HasResponseStarted)
                {
                    ThrowResponseAlreadyStartedException(nameof(OnStarting));
                }

                if (_onStarting == null)
                {
                    _onStarting = new Stack<KeyValuePair<Func<object, Task>, object>>();
                }
                _onStarting.Push(new KeyValuePair<Func<object, Task>, object>(callback, state));
            }
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
            lock (_onCompletedSync)
            {
                if (_onCompleted == null)
                {
                    _onCompleted = new Stack<KeyValuePair<Func<object, Task>, object>>();
                }
                _onCompleted.Push(new KeyValuePair<Func<object, Task>, object>(callback, state));
            }
        }

        protected async Task FireOnStarting()
        {
            Stack<KeyValuePair<Func<object, Task>, object>> onStarting = null;
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

        protected async Task FireOnCompleted()
        {
            Stack<KeyValuePair<Func<object, Task>, object>> onCompleted = null;
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
            InitializeResponse(0).GetAwaiter().GetResult();
            Output.Flush();
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            await InitializeResponse(0);
            await Output.FlushAsync(cancellationToken);
        }

        public void Write(ArraySegment<byte> data)
        {
            // For the first write, ensure headers are flushed if Write(Chunked) isn't called.
            var firstWrite = !HasResponseStarted;

            if (firstWrite)
            {
                InitializeResponse(data.Count).GetAwaiter().GetResult();
            }
            else
            {
                VerifyAndUpdateWrite(data.Count);
            }

            if (_canHaveBody)
            {
                if (_autoChunk)
                {
                    if (data.Count == 0)
                    {
                        if (firstWrite)
                        {
                            Flush();
                        }
                        return;
                    }
                    WriteChunked(data);
                }
                else
                {
                    CheckLastWrite();
                    Output.Write(data);
                }
            }
            else
            {
                HandleNonBodyResponseWrite();

                if (firstWrite)
                {
                    Flush();
                }
            }
        }

        public Task WriteAsync(ArraySegment<byte> data, CancellationToken cancellationToken)
        {
            if (!HasResponseStarted)
            {
                return WriteAsyncAwaited(data, cancellationToken);
            }

            VerifyAndUpdateWrite(data.Count);

            if (_canHaveBody)
            {
                if (_autoChunk)
                {
                    if (data.Count == 0)
                    {
                        return TaskCache.CompletedTask;
                    }
                    return WriteChunkedAsync(data, cancellationToken);
                }
                else
                {
                    CheckLastWrite();
                    return Output.WriteAsync(data, cancellationToken: cancellationToken);
                }
            }
            else
            {
                HandleNonBodyResponseWrite();
                return TaskCache.CompletedTask;
            }
        }

        public async Task WriteAsyncAwaited(ArraySegment<byte> data, CancellationToken cancellationToken)
        {
            await InitializeResponseAwaited(data.Count);

            // WriteAsyncAwaited is only called for the first write to the body.
            // Ensure headers are flushed if Write(Chunked)Async isn't called.
            if (_canHaveBody)
            {
                if (_autoChunk)
                {
                    if (data.Count == 0)
                    {
                        await FlushAsync(cancellationToken);
                        return;
                    }

                    await WriteChunkedAsync(data, cancellationToken);
                }
                else
                {
                    CheckLastWrite();
                    await Output.WriteAsync(data, cancellationToken: cancellationToken);
                }
            }
            else
            {
                HandleNonBodyResponseWrite();
                await FlushAsync(cancellationToken);
            }
        }

        private void VerifyAndUpdateWrite(int count)
        {
            var responseHeaders = FrameResponseHeaders;

            if (responseHeaders != null &&
                !responseHeaders.HasTransferEncoding &&
                responseHeaders.ContentLength.HasValue &&
                _responseBytesWritten + count > responseHeaders.ContentLength.Value)
            {
                _keepAlive = false;
                throw new InvalidOperationException(
                    $"Response Content-Length mismatch: too many bytes written ({_responseBytesWritten + count} of {responseHeaders.ContentLength.Value}).");
            }

            _responseBytesWritten += count;
        }

        private void CheckLastWrite()
        {
            var responseHeaders = FrameResponseHeaders;

            // Prevent firing request aborted token if this is the last write, to avoid
            // aborting the request if the app is still running when the client receives
            // the final bytes of the response and gracefully closes the connection.
            //
            // Called after VerifyAndUpdateWrite(), so _responseBytesWritten has already been updated.
            if (responseHeaders != null &&
                !responseHeaders.HasTransferEncoding &&
                responseHeaders.ContentLength.HasValue &&
                _responseBytesWritten == responseHeaders.ContentLength.Value)
            {
                _abortedCts = null;
            }
        }

        protected void VerifyResponseContentLength()
        {
            var responseHeaders = FrameResponseHeaders;

            if (!HttpMethods.IsHead(Method) &&
                !responseHeaders.HasTransferEncoding &&
                responseHeaders.ContentLength.HasValue &&
                _responseBytesWritten < responseHeaders.ContentLength.Value)
            {
                // We need to close the connection if any bytes were written since the client
                // cannot be certain of how many bytes it will receive.
                if (_responseBytesWritten > 0)
                {
                    _keepAlive = false;
                }

                ReportApplicationError(new InvalidOperationException(
                    $"Response Content-Length mismatch: too few bytes written ({_responseBytesWritten} of {responseHeaders.ContentLength.Value})."));
            }
        }

        private void WriteChunked(ArraySegment<byte> data)
        {
            Output.Write(data, chunk: true);
        }

        private Task WriteChunkedAsync(ArraySegment<byte> data, CancellationToken cancellationToken)
        {
            return Output.WriteAsync(data, chunk: true, cancellationToken: cancellationToken);
        }

        private Task WriteChunkedResponseSuffix()
        {
            return Output.WriteAsync(_endChunkedResponseBytes);
        }

        private static ArraySegment<byte> CreateAsciiByteArraySegment(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            return new ArraySegment<byte>(bytes);
        }

        public void ProduceContinue()
        {
            if (HasResponseStarted)
            {
                return;
            }

            StringValues expect;
            if (_httpVersion == Http.HttpVersion.Http11 &&
                RequestHeaders.TryGetValue("Expect", out expect) &&
                (expect.FirstOrDefault() ?? "").Equals("100-continue", StringComparison.OrdinalIgnoreCase))
            {
                Output.Write(_continueBytes);
            }
        }

        public Task InitializeResponse(int firstWriteByteCount)
        {
            if (HasResponseStarted)
            {
                return TaskCache.CompletedTask;
            }

            if (_onStarting != null)
            {
                return InitializeResponseAwaited(firstWriteByteCount);
            }

            if (_applicationException != null)
            {
                ThrowResponseAbortedException();
            }

            VerifyAndUpdateWrite(firstWriteByteCount);
            ProduceStart(appCompleted: false);

            return TaskCache.CompletedTask;
        }

        private async Task InitializeResponseAwaited(int firstWriteByteCount)
        {
            await FireOnStarting();

            if (_applicationException != null)
            {
                ThrowResponseAbortedException();
            }

            VerifyAndUpdateWrite(firstWriteByteCount);
            ProduceStart(appCompleted: false);
        }

        private void ProduceStart(bool appCompleted)
        {
            if (HasResponseStarted)
            {
                return;
            }

            _requestProcessingStatus = RequestProcessingStatus.ResponseStarted;

            var statusBytes = ReasonPhrases.ToStatusBytes(StatusCode, ReasonPhrase);

            CreateResponseHeader(statusBytes, appCompleted);
        }

        protected Task TryProduceInvalidRequestResponse()
        {
            if (_requestRejectedException != null)
            {
                if (FrameRequestHeaders == null || FrameResponseHeaders == null)
                {
                    InitializeHeaders();
                }

                return ProduceEnd();
            }

            return TaskCache.CompletedTask;
        }

        protected Task ProduceEnd()
        {
            if (_requestRejectedException != null || _applicationException != null)
            {
                if (HasResponseStarted)
                {
                    // We can no longer change the response, so we simply close the connection.
                    _requestProcessingStopping = true;
                    return TaskCache.CompletedTask;
                }

                // If the request was rejected, the error state has already been set by SetBadRequestState and
                // that should take precedence.
                if (_requestRejectedException != null)
                {
                    SetErrorResponseHeaders(statusCode: _requestRejectedException.StatusCode);
                }
                else
                {
                    // 500 Internal Server Error
                    SetErrorResponseHeaders(statusCode: StatusCodes.Status500InternalServerError);
                }
            }

            if (!HasResponseStarted)
            {
                return ProduceEndAwaited();
            }

            return WriteSuffix();
        }

        private async Task ProduceEndAwaited()
        {
            ProduceStart(appCompleted: true);

            // Force flush
            await Output.FlushAsync();

            await WriteSuffix();
        }

        private Task WriteSuffix()
        {
            // _autoChunk should be checked after we are sure ProduceStart() has been called
            // since ProduceStart() may set _autoChunk to true.
            if (_autoChunk)
            {
                return WriteAutoChunkSuffixAwaited();
            }

            if (_keepAlive)
            {
                ConnectionControl.End(ProduceEndType.ConnectionKeepAlive);
            }

            if (HttpMethods.IsHead(Method) && _responseBytesWritten > 0)
            {
                Log.ConnectionHeadResponseBodyWrite(ConnectionId, _responseBytesWritten);
            }

            return TaskCache.CompletedTask;
        }

        private async Task WriteAutoChunkSuffixAwaited()
        {
            // For the same reason we call CheckLastWrite() in Content-Length responses.
            _abortedCts = null;

            await WriteChunkedResponseSuffix();

            if (_keepAlive)
            {
                ConnectionControl.End(ProduceEndType.ConnectionKeepAlive);
            }
        }

        private void CreateResponseHeader(
            byte[] statusBytes,
            bool appCompleted)
        {
            var responseHeaders = FrameResponseHeaders;
            var hasConnection = responseHeaders.HasConnection;
            var connectionOptions = FrameHeaders.ParseConnection(responseHeaders.HeaderConnection);
            var hasTransferEncoding = responseHeaders.HasTransferEncoding;
            var transferCoding = FrameHeaders.GetFinalTransferCoding(responseHeaders.HeaderTransferEncoding);

            var end = Output.ProducingStart();

            if (_keepAlive && hasConnection)
            {
                _keepAlive = (connectionOptions & ConnectionOptions.KeepAlive) == ConnectionOptions.KeepAlive;
            }

            // https://tools.ietf.org/html/rfc7230#section-3.3.1
            // If any transfer coding other than
            // chunked is applied to a response payload body, the sender MUST either
            // apply chunked as the final transfer coding or terminate the message
            // by closing the connection.
            if (hasTransferEncoding && transferCoding != TransferCoding.Chunked)
            {
                _keepAlive = false;
            }

            // Set whether response can have body
            _canHaveBody = StatusCanHaveBody(StatusCode) && Method != "HEAD";

            // Don't set the Content-Length or Transfer-Encoding headers
            // automatically for HEAD requests or 204, 205, 304 responses.
            if (_canHaveBody)
            {
                if (!hasTransferEncoding && !responseHeaders.ContentLength.HasValue)
                {
                    if (appCompleted && StatusCode != StatusCodes.Status101SwitchingProtocols)
                    {
                        // Since the app has completed and we are only now generating
                        // the headers we can safely set the Content-Length to 0.
                        responseHeaders.ContentLength = 0;
                    }
                    else
                    {
                        // Note for future reference: never change this to set _autoChunk to true on HTTP/1.0
                        // connections, even if we were to infer the client supports it because an HTTP/1.0 request
                        // was received that used chunked encoding. Sending a chunked response to an HTTP/1.0
                        // client would break compliance with RFC 7230 (section 3.3.1):
                        //
                        // A server MUST NOT send a response containing Transfer-Encoding unless the corresponding
                        // request indicates HTTP/1.1 (or later).
                        if (_httpVersion == Http.HttpVersion.Http11 && StatusCode != StatusCodes.Status101SwitchingProtocols)
                        {
                            _autoChunk = true;
                            responseHeaders.SetRawTransferEncoding("chunked", _bytesTransferEncodingChunked);
                        }
                        else
                        {
                            _keepAlive = false;
                        }
                    }
                }
            }
            else if (hasTransferEncoding)
            {
                RejectNonBodyTransferEncodingResponse(appCompleted);
            }

            responseHeaders.SetReadOnly();

            if (!hasConnection)
            {
                if (!_keepAlive)
                {
                    responseHeaders.SetRawConnection("close", _bytesConnectionClose);
                }
                else if (_httpVersion == Http.HttpVersion.Http10)
                {
                    responseHeaders.SetRawConnection("keep-alive", _bytesConnectionKeepAlive);
                }
            }

            if (ServerOptions.AddServerHeader && !responseHeaders.HasServer)
            {
                responseHeaders.SetRawServer(Constants.ServerName, _bytesServer);
            }

            if (!responseHeaders.HasDate)
            {
                var dateHeaderValues = DateHeaderValueManager.GetDateHeaderValues();
                responseHeaders.SetRawDate(dateHeaderValues.String, dateHeaderValues.Bytes);
            }

            end.CopyFrom(_bytesHttpVersion11);
            end.CopyFrom(statusBytes);
            responseHeaders.CopyTo(ref end);
            end.CopyFrom(_bytesEndHeaders, 0, _bytesEndHeaders.Length);

            Output.ProducingComplete(end);
        }

        public bool TakeStartLineSpan(ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined)
        {
            var start = buffer.Start;
            var end = buffer.Start;

            examined = buffer.End;
            consumed = buffer.Start;

            if (_requestProcessingStatus == RequestProcessingStatus.RequestPending)
            {
                ConnectionControl.ResetTimeout(_requestHeadersTimeoutMilliseconds, TimeoutAction.SendTimeoutResponse);
            }

            _requestProcessingStatus = RequestProcessingStatus.RequestStarted;

            var limitedBuffer = buffer;
            if (buffer.Length >= ServerOptions.Limits.MaxRequestLineSize)
            {
                limitedBuffer = buffer.Slice(0, ServerOptions.Limits.MaxRequestLineSize);
            }
            if (ReadCursorOperations.Seek(limitedBuffer.Start, limitedBuffer.End, out end, ByteLF) == -1)
            {
                if (limitedBuffer.Length == ServerOptions.Limits.MaxRequestLineSize)
                {
                    RejectRequest(RequestRejectionReason.RequestLineTooLong);
                }
                else
                {
                    return false;
                }
            }

            const int stackAllocLimit = 512;

            // Move 1 byte past the \r
            end = limitedBuffer.Move(end, 1);
            var startLineBuffer = limitedBuffer.Slice(0, end);

            Span<byte> span;

            if (startLineBuffer.IsSingleSpan)
            {
                // No copies, directly use the one and only span
                span = startLineBuffer.ToSpan();
            }
            else if (startLineBuffer.Length < stackAllocLimit)
            {
                unsafe
                {
                    // Multiple buffers and < stackAllocLimit, copy into a stack buffer
                    byte* stackBuffer = stackalloc byte[startLineBuffer.Length];
                    span = new Span<byte>(stackBuffer, startLineBuffer.Length);
                    startLineBuffer.CopyTo(span);
                }
            }
            else
            {
                // We're not a single span here but we can use pooled arrays to avoid allocations in the rare case
                span = new Span<byte>(new byte[startLineBuffer.Length]);
                startLineBuffer.CopyTo(span);
            }

            var methodEnd = 0;
            if (!span.GetKnownMethod(out string method))
            {
                methodEnd = span.IndexOf(ByteSpace);
                if (methodEnd == -1)
                {
                    RejectRequestLine(start, end);
                }

                method = span.Slice(0, methodEnd).GetAsciiString();

                if (method == null)
                {
                    RejectRequestLine(start, end);
                }

                // Note: We're not in the fast path any more (GetKnownMethod should have handled any HTTP Method we're aware of)
                // So we can be a tiny bit slower and more careful here.
                for (int i = 0; i < method.Length; i++)
                {
                    if (!IsValidTokenChar(method[i]))
                    {
                        RejectRequestLine(start, end);
                    }
                }
            }
            else
            {
                methodEnd += method.Length;
            }

            var needDecode = false;
            var pathBegin = methodEnd + 1;
            var pathToEndSpan = span.Slice(pathBegin, span.Length - pathBegin);
            // var pbs = Encoding.UTF8.GetString(pathToEndSpan.ToArray());
            pathBegin = 0;

            // TODO: IndexOfAny
            var spaceIndex = pathToEndSpan.IndexOf(ByteSpace);
            var questionMarkIndex = pathToEndSpan.IndexOf(ByteQuestionMark);
            var percentageIndex = pathToEndSpan.IndexOf(BytePercentage);

            var pathEnd = MinNonZero(spaceIndex, questionMarkIndex, percentageIndex);

            if (spaceIndex == -1 && questionMarkIndex == -1 && percentageIndex == -1)
            {
                RejectRequestLine(start, end);
            }
            else if (percentageIndex != -1)
            {
                needDecode = true;

                pathEnd = MinNonZero(spaceIndex, questionMarkIndex);
                if (questionMarkIndex == -1 && spaceIndex == -1)
                {
                    RejectRequestLine(start, end);
                }
            }

            var queryString = "";
            var queryEnd = pathEnd;
            if (questionMarkIndex != -1)
            {
                queryEnd = spaceIndex;
                if (spaceIndex == -1)
                {
                    RejectRequestLine(start, end);
                }

                queryString = pathToEndSpan.Slice(pathEnd, queryEnd - pathEnd).GetAsciiString();
            }

            if (pathBegin == pathEnd)
            {
                RejectRequestLine(start, end);
            }

            var versionBegin = queryEnd + 1;
            var versionToEndSpan = pathToEndSpan.Slice(versionBegin, pathToEndSpan.Length - versionBegin);
            // var ves = Encoding.ASCII.GetString(versionToEndSpan.ToArray());
            versionBegin = 0;
            var versionEnd = versionToEndSpan.IndexOf(ByteCR);

            if (versionEnd == -1)
            {
                RejectRequestLine(start, end);
            }

            if (!versionToEndSpan.GetKnownVersion(out string httpVersion))
            {
                httpVersion = versionToEndSpan.Slice(0, versionEnd).GetAsciiString();

                if (httpVersion == null)
                {
                    RejectRequestLine(start, end);
                }
                else
                {
                    RejectRequest(RequestRejectionReason.UnrecognizedHTTPVersion, httpVersion);
                }
            }

            if (versionToEndSpan[versionEnd + 1] != ByteLF)
            {
                RejectRequestLine(start, end);
            }

            var pathBuffer = pathToEndSpan.Slice(pathBegin, pathEnd);
            var targetBuffer = pathToEndSpan.Slice(pathBegin, queryEnd);

            // URIs are always encoded/escaped to ASCII https://tools.ietf.org/html/rfc3986#page-11
            // Multibyte Internationalized Resource Identifiers (IRIs) are first converted to utf8;
            // then encoded/escaped to ASCII  https://www.ietf.org/rfc/rfc3987.txt "Mapping of IRIs to URIs"
            string requestUrlPath;
            string rawTarget;
            if (needDecode)
            {
                // Read raw target before mutating memory.
                rawTarget = targetBuffer.GetAsciiString() ?? string.Empty;

                // URI was encoded, unescape and then parse as utf8
                var pathSpan = pathBuffer;
                int pathLength = UrlEncoder.Decode(pathSpan, pathSpan);
                requestUrlPath = new Utf8String(pathSpan.Slice(0, pathLength)).ToString();
            }
            else
            {
                // URI wasn't encoded, parse as ASCII
                requestUrlPath = pathBuffer.GetAsciiString() ?? string.Empty;

                if (queryString.Length == 0)
                {
                    // No need to allocate an extra string if the path didn't need
                    // decoding and there's no query string following it.
                    rawTarget = requestUrlPath;
                }
                else
                {
                    rawTarget = targetBuffer.GetAsciiString() ?? string.Empty;
                }
            }

            var normalizedTarget = PathNormalizer.RemoveDotSegments(requestUrlPath);

            consumed = end;
            examined = end;
            Method = method;
            QueryString = queryString;
            RawTarget = rawTarget;
            HttpVersion = httpVersion;

            bool caseMatches;
            if (RequestUrlStartsWithPathBase(normalizedTarget, out caseMatches))
            {
                PathBase = caseMatches ? _pathBase : normalizedTarget.Substring(0, _pathBase.Length);
                Path = normalizedTarget.Substring(_pathBase.Length);
            }
            else if (rawTarget[0] == '/') // check rawTarget since normalizedTarget can be "" or "/" after dot segment removal
            {
                Path = normalizedTarget;
            }
            else
            {
                Path = string.Empty;
                PathBase = string.Empty;
                QueryString = string.Empty;
            }

            return true;
        }

        private int MinNonZero(int v1, int v2)
        {
            v1 = v1 == -1 ? int.MaxValue : v1;
            v2 = v2 == -1 ? int.MaxValue : v2;
            return Math.Min(v1, v2);
        }

        private int MinNonZero(int v1, int v2, int v3)
        {
            v1 = v1 == -1 ? int.MaxValue : v1;
            v2 = v2 == -1 ? int.MaxValue : v2;
            v3 = v3 == -1 ? int.MaxValue : v3;
            return Math.Min(Math.Min(v1, v2), v3);
        }

        public bool TakeStartLine(ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined)
        {
            var start = buffer.Start;
            var end = buffer.Start;

            examined = buffer.End;
            consumed = buffer.Start;

            if (_requestProcessingStatus == RequestProcessingStatus.RequestPending)
            {
                ConnectionControl.ResetTimeout(_requestHeadersTimeoutMilliseconds, TimeoutAction.SendTimeoutResponse);
            }

            _requestProcessingStatus = RequestProcessingStatus.RequestStarted;

            var limitedBuffer = buffer;
            if (buffer.Length >= ServerOptions.Limits.MaxRequestLineSize)
            {
                limitedBuffer = buffer.Slice(0, ServerOptions.Limits.MaxRequestLineSize);
            }
            if (ReadCursorOperations.Seek(limitedBuffer.Start, limitedBuffer.End, out end, ByteLF) == -1)
            {
                if (limitedBuffer.Length == ServerOptions.Limits.MaxRequestLineSize)
                {
                    RejectRequest(RequestRejectionReason.RequestLineTooLong);
                }
                else
                {
                    return false;
                }
            }

            end = buffer.Move(end, 1);
            ReadCursor methodEnd;
            string method;
            if (!buffer.GetKnownMethod(out method))
            {
                if (ReadCursorOperations.Seek(buffer.Start, end, out methodEnd, ByteSpace) == -1)
                {
                    RejectRequestLine(start, end);
                }

                method = buffer.Slice(buffer.Start, methodEnd).GetAsciiString();

                if (method == null)
                {
                    RejectRequestLine(start, end);
                }

                // Note: We're not in the fast path any more (GetKnownMethod should have handled any HTTP Method we're aware of)
                // So we can be a tiny bit slower and more careful here.
                for (int i = 0; i < method.Length; i++)
                {
                    if (!IsValidTokenChar(method[i]))
                    {
                        RejectRequestLine(start, end);
                    }
                }
            }
            else
            {
                methodEnd = buffer.Slice(method.Length).Start;
            }

            var needDecode = false;
            ReadCursor pathEnd;

            var pathBegin = buffer.Move(methodEnd, 1);

            var chFound = ReadCursorOperations.Seek(pathBegin, end, out pathEnd, ByteSpace, ByteQuestionMark, BytePercentage);
            if (chFound == -1)
            {
                RejectRequestLine(start, end);
            }
            else if (chFound == BytePercentage)
            {
                needDecode = true;
                chFound = ReadCursorOperations.Seek(pathBegin, end, out pathEnd, ByteSpace, ByteQuestionMark);
                if (chFound == -1)
                {
                    RejectRequestLine(start, end);
                }
            }

            var queryString = "";
            ReadCursor queryEnd = pathEnd;
            if (chFound == ByteQuestionMark)
            {
                if (ReadCursorOperations.Seek(pathEnd, end, out queryEnd, ByteSpace) == -1)
                {
                    RejectRequestLine(start, end);
                }
                queryString = buffer.Slice(pathEnd, queryEnd).GetAsciiString();
            }

            // No path
            if (pathBegin == pathEnd)
            {
                RejectRequestLine(start, end);
            }

            ReadCursor versionEnd;
            if (ReadCursorOperations.Seek(queryEnd, end, out versionEnd, ByteCR) == -1)
            {
                RejectRequestLine(start, end);
            }

            string httpVersion;
            var versionBuffer = buffer.Slice(queryEnd, end).Slice(1);
            if (!versionBuffer.GetKnownVersion(out httpVersion))
            {
                httpVersion = versionBuffer.Start.GetAsciiStringEscaped(versionEnd, 9);

                if (httpVersion == string.Empty)
                {
                    RejectRequestLine(start, end);
                }
                else
                {
                    RejectRequest(RequestRejectionReason.UnrecognizedHTTPVersion, httpVersion);
                }
            }

            var lineEnd = buffer.Slice(versionEnd, 2).ToSpan();
            if (lineEnd[1] != ByteLF)
            {
                RejectRequestLine(start, end);
            }

            var pathBuffer = buffer.Slice(pathBegin, pathEnd);
            var targetBuffer = buffer.Slice(pathBegin, queryEnd);

            // URIs are always encoded/escaped to ASCII https://tools.ietf.org/html/rfc3986#page-11
            // Multibyte Internationalized Resource Identifiers (IRIs) are first converted to utf8;
            // then encoded/escaped to ASCII  https://www.ietf.org/rfc/rfc3987.txt "Mapping of IRIs to URIs"
            string requestUrlPath;
            string rawTarget;
            if (needDecode)
            {
                // Read raw target before mutating memory.
                rawTarget = targetBuffer.GetAsciiString() ?? string.Empty;

                // URI was encoded, unescape and then parse as utf8
                var pathSpan = pathBuffer.ToSpan();
                int pathLength = UrlEncoder.Decode(pathSpan, pathSpan);
                requestUrlPath = new Utf8String(pathSpan.Slice(0, pathLength)).ToString();
            }
            else
            {
                // URI wasn't encoded, parse as ASCII
                requestUrlPath = pathBuffer.GetAsciiString() ?? string.Empty;

                if (queryString.Length == 0)
                {
                    // No need to allocate an extra string if the path didn't need
                    // decoding and there's no query string following it.
                    rawTarget = requestUrlPath;
                }
                else
                {
                    rawTarget = targetBuffer.GetAsciiString() ?? string.Empty;
                }
            }

            var normalizedTarget = PathNormalizer.RemoveDotSegments(requestUrlPath);

            consumed = end;
            examined = end;
            Method = method;
            QueryString = queryString;
            RawTarget = rawTarget;
            HttpVersion = httpVersion;

            bool caseMatches;
            if (RequestUrlStartsWithPathBase(normalizedTarget, out caseMatches))
            {
                PathBase = caseMatches ? _pathBase : normalizedTarget.Substring(0, _pathBase.Length);
                Path = normalizedTarget.Substring(_pathBase.Length);
            }
            else if (rawTarget[0] == '/') // check rawTarget since normalizedTarget can be "" or "/" after dot segment removal
            {
                Path = normalizedTarget;
            }
            else
            {
                Path = string.Empty;
                PathBase = string.Empty;
                QueryString = string.Empty;
            }

            return true;
        }

        private void RejectRequestLine(ReadCursor start, ReadCursor end)
        {
            const int MaxRequestLineError = 32;
            RejectRequest(RequestRejectionReason.InvalidRequestLine,
                           Log.IsEnabled(LogLevel.Information) ? start.GetAsciiStringEscaped(end, MaxRequestLineError) : string.Empty);
        }

        private static bool IsValidTokenChar(char c)
        {
            // Determines if a character is valid as a 'token' as defined in the
            // HTTP spec: https://tools.ietf.org/html/rfc7230#section-3.2.6
            return
                (c >= '0' && c <= '9') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                c == '!' ||
                c == '#' ||
                c == '$' ||
                c == '%' ||
                c == '&' ||
                c == '\'' ||
                c == '*' ||
                c == '+' ||
                c == '-' ||
                c == '.' ||
                c == '^' ||
                c == '_' ||
                c == '`' ||
                c == '|' ||
                c == '~';
        }

        private bool RequestUrlStartsWithPathBase(string requestUrl, out bool caseMatches)
        {
            caseMatches = true;

            if (string.IsNullOrEmpty(_pathBase))
            {
                return false;
            }

            if (requestUrl.Length < _pathBase.Length || (requestUrl.Length > _pathBase.Length && requestUrl[_pathBase.Length] != '/'))
            {
                return false;
            }

            for (var i = 0; i < _pathBase.Length; i++)
            {
                if (requestUrl[i] != _pathBase[i])
                {
                    if (char.ToLowerInvariant(requestUrl[i]) == char.ToLowerInvariant(_pathBase[i]))
                    {
                        caseMatches = false;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public bool TakeMessageHeadersSpan(ReadableBuffer buffer, FrameRequestHeaders requestHeaders, out ReadCursor consumed, out ReadCursor examined)
        {
            consumed = buffer.Start;
            examined = buffer.End;

            var bufferLength = buffer.Length;
            var reader = new ReadableBufferReader(buffer);

            while (true)
            {
                var start = reader;
                int ch1 = reader.Take();
                var ch2 = reader.Take();

                if (ch1 == -1)
                {
                    return false;
                }

                if (ch1 == ByteCR)
                {
                    // Check for final CRLF.
                    if (ch2 == -1)
                    {
                        return false;
                    }
                    else if (ch2 == ByteLF)
                    {
                        consumed = reader.Cursor;
                        examined = consumed;
                        ConnectionControl.CancelTimeout();
                        return true;
                    }

                    // Headers don't end in CRLF line.
                    RejectRequest(RequestRejectionReason.HeadersCorruptedInvalidHeaderSequence);
                }
                else if (ch1 == ByteSpace || ch1 == ByteTab)
                {
                    RejectRequest(RequestRejectionReason.HeaderLineMustNotStartWithWhitespace);
                }

                // If we've parsed the max allowed numbers of headers and we're starting a new
                // one, we've gone over the limit.
                if (_requestHeadersParsed == ServerOptions.Limits.MaxRequestHeaderCount)
                {
                    RejectRequest(RequestRejectionReason.TooManyHeaders);
                }

                // Reset the reader since we're not at the end of headers
                reader = start;

                // Now parse a single header
                ReadCursor lineEnd;
                var limitedBuffer = buffer;
                if (bufferLength >= _remainingRequestHeadersBytesAllowed)
                {
                    limitedBuffer = buffer.Slice(consumed, _remainingRequestHeadersBytesAllowed);
                }

                if (ReadCursorOperations.Seek(consumed, limitedBuffer.End, out lineEnd, ByteLF) == -1)
                {
                    if (limitedBuffer.Length == _remainingRequestHeadersBytesAllowed)
                    {
                        RejectRequest(RequestRejectionReason.HeadersExceedMaxTotalSize);
                    }
                    else
                    {
                        return false;
                    }
                }

                const int stackAllocLimit = 512;

                // TODO: Find a better way to move 2 positions if possible
                lineEnd = limitedBuffer.Move(lineEnd, 1);

                if (lineEnd != limitedBuffer.End)
                {
                    lineEnd = limitedBuffer.Move(lineEnd, 1);
                }

                var headerBuffer = limitedBuffer.Slice(consumed, lineEnd);

                Span<byte> span;
                if (headerBuffer.IsSingleSpan)
                {
                    // No copies, directly use the one and only span
                    span = headerBuffer.ToSpan();
                }
                else if (headerBuffer.Length < stackAllocLimit)
                {
                    unsafe
                    {
                        // Multiple buffers and < stackAllocLimit, copy into a stack buffer
                        byte* stackBuffer = stackalloc byte[headerBuffer.Length];
                        span = new Span<byte>(stackBuffer, headerBuffer.Length);
                        headerBuffer.CopyTo(span);
                    }
                }
                else
                {
                    // We're not a single span here but we can use pooled arrays to avoid allocations in the rare case
                    span = new Span<byte>(new byte[headerBuffer.Length]);
                    headerBuffer.CopyTo(span);
                }

                int endNameIndex = span.IndexOf(ByteColon);
                if (endNameIndex == -1)
                {
                    RejectRequest(RequestRejectionReason.NoColonCharacterFoundInHeaderLine);
                }

                var nameBuffer = span.Slice(0, endNameIndex);
                // var nbs = nameBuffer.GetAsciiString();
                if (nameBuffer.IndexOf(ByteSpace) != -1 || nameBuffer.IndexOf(ByteTab) != -1)
                {
                    RejectRequest(RequestRejectionReason.WhitespaceIsNotAllowedInHeaderName);
                }

                int endValueIndex = span.IndexOf(ByteCR);
                if (endValueIndex == -1)
                {
                    RejectRequest(RequestRejectionReason.MissingCRInHeaderLine);
                }

                var lineSuffix = span.Slice(endValueIndex);
                if (lineSuffix.Length < 3)
                {
                    return false;
                }

                // lineSuffix = lineSuffix.Slice(0, 3); // \r\n\r
                // var lineSufixSpan = lineSuffix;
                // This check and MissingCRInHeaderLine is a bit backwards, we should do it at once instead of having another seek
                if (lineSuffix[1] != ByteLF)
                {
                    RejectRequest(RequestRejectionReason.HeaderValueMustNotContainCR);
                }

                var next = lineSuffix[2];
                if (next == ByteSpace || next == ByteTab)
                {
                    // From https://tools.ietf.org/html/rfc7230#section-3.2.4:
                    //
                    // Historically, HTTP header field values could be extended over
                    // multiple lines by preceding each extra line with at least one space
                    // or horizontal tab (obs-fold).  This specification deprecates such
                    // line folding except within the message/http media type
                    // (Section 8.3.1).  A sender MUST NOT generate a message that includes
                    // line folding (i.e., that has any field-value that contains a match to
                    // the obs-fold rule) unless the message is intended for packaging
                    // within the message/http media type.
                    //
                    // A server that receives an obs-fold in a request message that is not
                    // within a message/http container MUST either reject the message by
                    // sending a 400 (Bad Request), preferably with a representation
                    // explaining that obsolete line folding is unacceptable, or replace
                    // each received obs-fold with one or more SP octets prior to
                    // interpreting the field value or forwarding the message downstream.
                    RejectRequest(RequestRejectionReason.HeaderValueLineFoldingNotSupported);
                }

                // Trim trailing whitespace from header value by repeatedly advancing to next
                // whitespace or CR.
                //
                // - If CR is found, this is the end of the header value.
                // - If whitespace is found, this is the _tentative_ end of the header value.
                //   If non-whitespace is found after it and it's not CR, seek again to the next
                //   whitespace or CR for a new (possibly tentative) end of value.

                var valueBuffer = span.Slice(endNameIndex + 1, endValueIndex - (endNameIndex + 1));

                // TODO: Trim else where
                var value = valueBuffer.GetAsciiString().Trim();

                var headerLineLength = span.Length - 1;

                _remainingRequestHeadersBytesAllowed -= headerLineLength;
                bufferLength -= headerLineLength;

                _requestHeadersParsed++;

                requestHeaders.Append(nameBuffer, value);

                // -1 so that we can re-check the extra \r
                reader.Skip(headerLineLength);
                consumed = reader.Cursor;
            }
        }

        public bool TakeMessageHeaders(ReadableBuffer buffer, FrameRequestHeaders requestHeaders, out ReadCursor consumed, out ReadCursor examined)
        {
            consumed = buffer.Start;
            examined = buffer.End;

            while (true)
            {
                var headersEnd = buffer.Slice(0, Math.Min(buffer.Length, 2));
                var headersEndSpan = headersEnd.ToSpan();

                if (headersEndSpan.Length == 0)
                {
                    return false;
                }
                else
                {
                    var ch = headersEndSpan[0];
                    if (ch == ByteCR)
                    {
                        // Check for final CRLF.
                        if (headersEndSpan.Length < 2)
                        {
                            return false;
                        }
                        else if (headersEndSpan[1] == ByteLF)
                        {
                            consumed = headersEnd.End;
                            examined = consumed;
                            ConnectionControl.CancelTimeout();
                            return true;
                        }

                        // Headers don't end in CRLF line.
                        RejectRequest(RequestRejectionReason.HeadersCorruptedInvalidHeaderSequence);
                    }
                    else if (ch == ByteSpace || ch == ByteTab)
                    {
                        RejectRequest(RequestRejectionReason.HeaderLineMustNotStartWithWhitespace);
                    }
                }

                // If we've parsed the max allowed numbers of headers and we're starting a new
                // one, we've gone over the limit.
                if (_requestHeadersParsed == ServerOptions.Limits.MaxRequestHeaderCount)
                {
                    RejectRequest(RequestRejectionReason.TooManyHeaders);
                }

                ReadCursor lineEnd;
                var limitedBuffer = buffer;
                if (buffer.Length >= _remainingRequestHeadersBytesAllowed)
                {
                    limitedBuffer = buffer.Slice(0, _remainingRequestHeadersBytesAllowed);
                }
                if (ReadCursorOperations.Seek(limitedBuffer.Start, limitedBuffer.End, out lineEnd, ByteLF) == -1)
                {
                    if (limitedBuffer.Length == _remainingRequestHeadersBytesAllowed)
                    {
                        RejectRequest(RequestRejectionReason.HeadersExceedMaxTotalSize);
                    }
                    else
                    {
                        return false;
                    }
                }

                var beginName = buffer.Start;
                ReadCursor endName;
                if (ReadCursorOperations.Seek(buffer.Start, lineEnd, out endName, ByteColon) == -1)
                {
                    RejectRequest(RequestRejectionReason.NoColonCharacterFoundInHeaderLine);
                }

                ReadCursor whitespace;
                if (ReadCursorOperations.Seek(beginName, endName, out whitespace, ByteTab, ByteSpace) != -1)
                {
                    RejectRequest(RequestRejectionReason.WhitespaceIsNotAllowedInHeaderName);
                }

                ReadCursor endValue;
                if (ReadCursorOperations.Seek(beginName, lineEnd, out endValue, ByteCR) == -1)
                {
                    RejectRequest(RequestRejectionReason.MissingCRInHeaderLine);
                }

                var lineSufix = buffer.Slice(endValue);
                if (lineSufix.Length < 3)
                {
                    return false;
                }
                lineSufix = lineSufix.Slice(0, 3); // \r\n\r
                var lineSufixSpan = lineSufix.ToSpan();
                // This check and MissingCRInHeaderLine is a bit backwards, we should do it at once instead of having another seek
                if (lineSufixSpan[1] != ByteLF)
                {
                    RejectRequest(RequestRejectionReason.HeaderValueMustNotContainCR);
                }

                var next = lineSufixSpan[2];
                if (next == ByteSpace || next == ByteTab)
                {
                    // From https://tools.ietf.org/html/rfc7230#section-3.2.4:
                    //
                    // Historically, HTTP header field values could be extended over
                    // multiple lines by preceding each extra line with at least one space
                    // or horizontal tab (obs-fold).  This specification deprecates such
                    // line folding except within the message/http media type
                    // (Section 8.3.1).  A sender MUST NOT generate a message that includes
                    // line folding (i.e., that has any field-value that contains a match to
                    // the obs-fold rule) unless the message is intended for packaging
                    // within the message/http media type.
                    //
                    // A server that receives an obs-fold in a request message that is not
                    // within a message/http container MUST either reject the message by
                    // sending a 400 (Bad Request), preferably with a representation
                    // explaining that obsolete line folding is unacceptable, or replace
                    // each received obs-fold with one or more SP octets prior to
                    // interpreting the field value or forwarding the message downstream.
                    RejectRequest(RequestRejectionReason.HeaderValueLineFoldingNotSupported);
                }

                // Trim trailing whitespace from header value by repeatedly advancing to next
                // whitespace or CR.
                //
                // - If CR is found, this is the end of the header value.
                // - If whitespace is found, this is the _tentative_ end of the header value.
                //   If non-whitespace is found after it and it's not CR, seek again to the next
                //   whitespace or CR for a new (possibly tentative) end of value.

                var nameBuffer = buffer.Slice(beginName, endName);

                // TODO: TrimStart and TrimEnd are pretty slow
                var valueBuffer = buffer.Slice(endName, endValue).Slice(1).TrimStart().TrimEnd();

                var name = nameBuffer.ToSpan();
                var value = valueBuffer.GetAsciiString();

                lineEnd = limitedBuffer.Move(lineEnd, 1);

                // TODO: bad
                _remainingRequestHeadersBytesAllowed -= buffer.Slice(0, lineEnd).Length;
                _requestHeadersParsed++;

                requestHeaders.Append(name, value);
                buffer = buffer.Slice(lineEnd);
                consumed = buffer.Start;
            }
        }

        public bool StatusCanHaveBody(int statusCode)
        {
            // List of status codes taken from Microsoft.Net.Http.Server.Response
            return statusCode != StatusCodes.Status204NoContent &&
                   statusCode != StatusCodes.Status205ResetContent &&
                   statusCode != StatusCodes.Status304NotModified;
        }

        private void ThrowResponseAlreadyStartedException(string value)
        {
            throw new InvalidOperationException($"{value} cannot be set, response has already started.");
        }

        private void RejectNonBodyTransferEncodingResponse(bool appCompleted)
        {
            var ex = new InvalidOperationException($"Transfer-Encoding set on a {StatusCode} non-body request.");
            if (!appCompleted)
            {
                // Back out of header creation surface exeception in user code
                _requestProcessingStatus = RequestProcessingStatus.RequestStarted;
                throw ex;
            }
            else
            {
                ReportApplicationError(ex);

                // 500 Internal Server Error
                SetErrorResponseHeaders(statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        private void SetErrorResponseHeaders(int statusCode)
        {
            Debug.Assert(!HasResponseStarted, $"{nameof(SetErrorResponseHeaders)} called after response had already started.");

            StatusCode = statusCode;
            ReasonPhrase = null;

            if (FrameResponseHeaders == null)
            {
                InitializeHeaders();
            }

            var responseHeaders = FrameResponseHeaders;
            responseHeaders.Reset();
            var dateHeaderValues = DateHeaderValueManager.GetDateHeaderValues();

            responseHeaders.SetRawDate(dateHeaderValues.String, dateHeaderValues.Bytes);
            responseHeaders.ContentLength = 0;

            if (ServerOptions.AddServerHeader)
            {
                responseHeaders.SetRawServer(Constants.ServerName, _bytesServer);
            }
        }

        public void HandleNonBodyResponseWrite()
        {
            // Writes to HEAD response are ignored and logged at the end of the request
            if (Method != "HEAD")
            {
                // Throw Exception for 204, 205, 304 responses.
                throw new InvalidOperationException($"Write to non-body {StatusCode} response.");
            }
        }

        private void ThrowResponseAbortedException()
        {
            throw new ObjectDisposedException(
                    "The response has been aborted due to an unhandled application exception.",
                    _applicationException);
        }

        public void RejectRequest(RequestRejectionReason reason)
        {
            RejectRequest(BadHttpRequestException.GetException(reason));
        }

        public void RejectRequest(RequestRejectionReason reason, string value)
        {
            RejectRequest(BadHttpRequestException.GetException(reason, value));
        }

        private void RejectRequest(BadHttpRequestException ex)
        {
            Log.ConnectionBadRequest(ConnectionId, ex);
            throw ex;
        }

        public void SetBadRequestState(RequestRejectionReason reason)
        {
            SetBadRequestState(BadHttpRequestException.GetException(reason));
        }

        public void SetBadRequestState(BadHttpRequestException ex)
        {
            if (!HasResponseStarted)
            {
                SetErrorResponseHeaders(ex.StatusCode);
            }

            _keepAlive = false;
            _requestProcessingStopping = true;
            _requestRejectedException = ex;
        }

        protected void ReportApplicationError(Exception ex)
        {
            if (_applicationException == null)
            {
                _applicationException = ex;
            }
            else if (_applicationException is AggregateException)
            {
                _applicationException = new AggregateException(_applicationException, ex).Flatten();
            }
            else
            {
                _applicationException = new AggregateException(_applicationException, ex);
            }

            Log.ApplicationError(ConnectionId, ex);
        }

        public enum RequestLineStatus
        {
            Empty,
            Incomplete,
            Done
        }

        private enum RequestProcessingStatus
        {
            RequestPending,
            RequestStarted,
            ResponseStarted
        }
    }
}
