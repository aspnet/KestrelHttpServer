// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

// ReSharper disable AccessToModifiedClosure

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public abstract partial class HttpProtocol : IHttpResponseControl
    {
        private static readonly byte[] _bytesConnectionClose = Encoding.ASCII.GetBytes("\r\nConnection: close");
        private static readonly byte[] _bytesConnectionKeepAlive = Encoding.ASCII.GetBytes("\r\nConnection: keep-alive");
        private static readonly byte[] _bytesTransferEncodingChunked = Encoding.ASCII.GetBytes("\r\nTransfer-Encoding: chunked");
        private static readonly byte[] _bytesServer = Encoding.ASCII.GetBytes("\r\nServer: " + Constants.ServerName);
        private static readonly Action<PipeWriter, ReadOnlyMemory<byte>> _writeChunk = WriteChunk;

        private readonly object _onStartingSync = new Object();
        private readonly object _onCompletedSync = new Object();

        protected Streams _streams;

        protected Stack<KeyValuePair<Func<object, Task>, object>> _onStarting;
        protected Stack<KeyValuePair<Func<object, Task>, object>> _onCompleted;

        protected volatile int _requestAborted;
        protected CancellationTokenSource _abortedCts;
        private CancellationToken? _manuallySetRequestAbortToken;

        protected RequestProcessingStatus _requestProcessingStatus;
        protected volatile bool _keepAlive; // volatile, see: https://msdn.microsoft.com/en-us/library/x13ttww7.aspx
        protected bool _upgradeAvailable;
        private bool _canHaveBody;
        private bool _autoChunk;
        protected Exception _applicationException;
        private BadHttpRequestException _requestRejectedException;

        protected HttpVersion _httpVersion;

        private string _requestId;
        protected int _requestHeadersParsed;

        protected long _responseBytesWritten;

        private readonly IHttpProtocolContext _context;

        protected string _methodText = null;
        private string _scheme = null;

        public HttpProtocol(IHttpProtocolContext context)
        {
            _context = context;

            ServerOptions = ServiceContext.ServerOptions;
            HttpResponseControl = this;
            RequestBodyPipe = CreateRequestBodyPipe();
        }

        public IHttpResponseControl HttpResponseControl { get; set; }

        public Pipe RequestBodyPipe { get; }

        public ServiceContext ServiceContext => _context.ServiceContext;
        private IPEndPoint LocalEndPoint => _context.LocalEndPoint;
        private IPEndPoint RemoteEndPoint => _context.RemoteEndPoint;

        public IFeatureCollection ConnectionFeatures => _context.ConnectionFeatures;
        public IHttpOutputProducer Output { get; protected set; }

        protected IKestrelTrace Log => ServiceContext.Log;
        private DateHeaderValueManager DateHeaderValueManager => ServiceContext.DateHeaderValueManager;
        // Hold direct reference to ServerOptions since this is used very often in the request processing path
        protected KestrelServerOptions ServerOptions { get; }
        protected string ConnectionId => _context.ConnectionId;

        public string ConnectionIdFeature { get; set; }
        public bool HasStartedConsumingRequestBody { get; set; }
        public long? MaxRequestBodySize { get; set; }
        public bool AllowSynchronousIO { get; set; }

        /// <summary>
        /// The request id. <seealso cref="HttpContext.TraceIdentifier"/>
        /// </summary>
        public string TraceIdentifier
        {
            set => _requestId = value;
            get
            {
                // don't generate an ID until it is requested
                if (_requestId == null)
                {
                    _requestId = CreateRequestId();
                }
                return _requestId;
            }
        }

        public abstract bool IsUpgradableRequest { get; }
        public bool IsUpgraded { get; set; }
        public IPAddress RemoteIpAddress { get; set; }
        public int RemotePort { get; set; }
        public IPAddress LocalIpAddress { get; set; }
        public int LocalPort { get; set; }
        public string Scheme { get; set; }
        public HttpMethod Method { get; set; }
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
                    return HttpUtilities.Http11Version;
                }
                if (_httpVersion == Http.HttpVersion.Http10)
                {
                    return HttpUtilities.Http10Version;
                }
                if (_httpVersion == Http.HttpVersion.Http2)
                {
                    return HttpUtilities.Http2Version;
                }

                return string.Empty;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                // GetKnownVersion returns versions which ReferenceEquals interned string
                // As most common path, check for this only in fast-path and inline
                if (ReferenceEquals(value, HttpUtilities.Http11Version))
                {
                    _httpVersion = Http.HttpVersion.Http11;
                }
                else if (ReferenceEquals(value, HttpUtilities.Http10Version))
                {
                    _httpVersion = Http.HttpVersion.Http10;
                }
                else if (ReferenceEquals(value, HttpUtilities.Http2Version))
                {
                    _httpVersion = Http.HttpVersion.Http2;
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
            if (value == HttpUtilities.Http11Version)
            {
                _httpVersion = Http.HttpVersion.Http11;
            }
            else if (value == HttpUtilities.Http10Version)
            {
                _httpVersion = Http.HttpVersion.Http10;
            }
            else if (value == HttpUtilities.Http2Version)
            {
                _httpVersion = Http.HttpVersion.Http2;
            }
            else
            {
                _httpVersion = Http.HttpVersion.Unknown;
            }
        }

        public IHeaderDictionary RequestHeaders { get; set; }
        public Stream RequestBody { get; set; }

        private int _statusCode;
        public int StatusCode
        {
            get => _statusCode;
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
            get => _reasonPhrase;

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
                    (_requestAborted == 1) ? new CancellationToken(true) :
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

                if (_requestAborted == 1)
                {
                    cts.Cancel();
                }
                return cts;
            }
        }

        public bool HasResponseStarted => _requestProcessingStatus == RequestProcessingStatus.ResponseStarted;

        protected HttpRequestHeaders HttpRequestHeaders { get; } = new HttpRequestHeaders();

        protected HttpResponseHeaders HttpResponseHeaders { get; } = new HttpResponseHeaders();

        public MinDataRate MinRequestBodyDataRate { get; set; }

        public MinDataRate MinResponseDataRate { get; set; }

        public void InitializeStreams(MessageBody messageBody)
        {
            if (_streams == null)
            {
                _streams = new Streams(bodyControl: this, httpResponseControl: this);
            }

            (RequestBody, ResponseBody) = _streams.Start(messageBody);
        }

        public void PauseStreams() => _streams.Pause();

        public void StopStreams() => _streams.Stop();

        // For testing
        internal void ResetState()
        {
            _requestProcessingStatus = RequestProcessingStatus.RequestPending;
        }

        public void Reset()
        {
            _onStarting = null;
            _onCompleted = null;

            _requestProcessingStatus = RequestProcessingStatus.RequestPending;
            _autoChunk = false;
            _applicationException = null;
            _requestRejectedException = null;

            ResetFeatureCollection();

            HasStartedConsumingRequestBody = false;
            MaxRequestBodySize = ServerOptions.Limits.MaxRequestBodySize;
            AllowSynchronousIO = ServerOptions.AllowSynchronousIO;
            TraceIdentifier = null;
            Method = HttpMethod.None;
            _methodText = null;
            PathBase = null;
            Path = null;
            RawTarget = null;
            QueryString = null;
            _httpVersion = Http.HttpVersion.Unknown;
            _statusCode = StatusCodes.Status200OK;
            _reasonPhrase = null;

            var remoteEndPoint = RemoteEndPoint;
            RemoteIpAddress = remoteEndPoint?.Address;
            RemotePort = remoteEndPoint?.Port ?? 0;

            var localEndPoint = LocalEndPoint;
            LocalIpAddress = localEndPoint?.Address;
            LocalPort = localEndPoint?.Port ?? 0;

            ConnectionIdFeature = ConnectionId;

            HttpRequestHeaders.Reset();
            HttpResponseHeaders.Reset();
            RequestHeaders = HttpRequestHeaders;
            ResponseHeaders = HttpResponseHeaders;

            if (_scheme == null)
            {
                var tlsFeature = ConnectionFeatures?[typeof(ITlsConnectionFeature)];
                _scheme = tlsFeature != null ? "https" : "http";
            }

            Scheme = _scheme;

            _manuallySetRequestAbortToken = null;
            _abortedCts = null;

            // Allow two bytes for \r\n after headers
            _requestHeadersParsed = 0;

            _responseBytesWritten = 0;

            MinRequestBodyDataRate = ServerOptions.Limits.MinRequestBodyDataRate;
            MinResponseDataRate = ServerOptions.Limits.MinResponseDataRate;

            OnReset();
        }

        protected abstract void OnReset();

        protected virtual void OnRequestProcessingEnding()
        {
        }

        protected virtual void OnRequestProcessingEnded()
        {
        }

        protected virtual void BeginRequestProcessing()
        {
        }

        protected virtual bool BeginRead(out ValueTask<ReadResult> awaitable)
        {
            awaitable = default;
            return false;
        }

        protected abstract string CreateRequestId();

        protected abstract MessageBody CreateMessageBody();

        protected abstract bool TryParseRequest(ReadResult result, out bool endConnection);

        private void CancelRequestAbortedToken()
        {
            try
            {
                RequestAbortedSource.Cancel();
                _abortedCts = null;
            }
            catch (Exception ex)
            {
                Log.ApplicationError(ConnectionId, TraceIdentifier, ex);
            }
        }

        /// <summary>
        /// Immediate kill the connection and poison the request and response streams.
        /// </summary>
        public void Abort(Exception error)
        {
            if (Interlocked.Exchange(ref _requestAborted, 1) == 0)
            {
                _keepAlive = false;

                _streams?.Abort(error);

                Output.Abort(error);

                // Potentially calling user code. CancelRequestAbortedToken logs any exceptions.
                ServiceContext.Scheduler.Schedule(state => ((HttpProtocol)state).CancelRequestAbortedToken(), this);
            }
        }

        public void OnHeader(Span<byte> name, Span<byte> value)
        {
            _requestHeadersParsed++;
            if (_requestHeadersParsed > ServerOptions.Limits.MaxRequestHeaderCount)
            {
                BadHttpRequestException.Throw(RequestRejectionReason.TooManyHeaders);
            }
            var valueString = value.GetAsciiStringNonNullCharacters();

            HttpRequestHeaders.Append(name, valueString);
        }

        public async Task ProcessRequestsAsync<TContext>(IHttpApplication<TContext> application)
        {
            try
            {
                await ProcessRequests(application);
            }
            catch (BadHttpRequestException ex)
            {
                // Handle BadHttpRequestException thrown during request line or header parsing.
                // SetBadRequestState logs the error.
                SetBadRequestState(ex);
            }
            catch (ConnectionResetException ex)
            {
                // Don't log ECONNRESET errors made between requests. Browsers like IE will reset connections regularly.
                if (_requestProcessingStatus != RequestProcessingStatus.RequestPending)
                {
                    Log.RequestProcessingError(ConnectionId, ex);
                }
            }
            catch (IOException ex)
            {
                Log.RequestProcessingError(ConnectionId, ex);
            }
            catch (Exception ex)
            {
                Log.LogWarning(0, ex, CoreStrings.RequestProcessingEndError);
            }
            finally
            {
                try
                {
                    OnRequestProcessingEnding();
                    await TryProduceInvalidRequestResponse();
                    Output.Dispose();
                }
                catch (Exception ex)
                {
                    Log.LogWarning(0, ex, CoreStrings.ConnectionShutdownError);
                }
                finally
                {
                    OnRequestProcessingEnded();
                }
            }
        }

        private async Task ProcessRequests<TContext>(IHttpApplication<TContext> application)
        {
            // Keep-alive is default for HTTP/1.1 and HTTP/2; parsing and errors will change its value
            _keepAlive = true;
            do
            {
                BeginRequestProcessing();

                var result = default(ReadResult);
                var endConnection = false;
                do
                {
                    if (BeginRead(out var awaitable))
                    {
                        result = await awaitable;
                    }
                } while (!TryParseRequest(result, out endConnection));

                if (endConnection)
                {
                    // Connection finished, stop processing requests
                    return;
                }

                var messageBody = CreateMessageBody();
                if (!messageBody.RequestKeepAlive)
                {
                    _keepAlive = false;
                }

                _upgradeAvailable = messageBody.RequestUpgrade;

                InitializeStreams(messageBody);

                var httpContext = application.CreateContext(this);

                BadHttpRequestException badRequestException = null;
                try
                {
                    KestrelEventSource.Log.RequestStart(this);

                    // Run the application code for this request
                    await application.ProcessRequestAsync(httpContext);

                    if (_requestAborted == 0)
                    {
                        VerifyResponseContentLength();
                    }
                }
                catch (Exception ex)
                {
                    ReportApplicationError(ex);
                    // Capture BadHttpRequestException for further processing
                    badRequestException = ex as BadHttpRequestException;
                }

                KestrelEventSource.Log.RequestStop(this);

                // Trigger OnStarting if it hasn't been called yet and the app hasn't
                // already failed. If an OnStarting callback throws we can go through
                // our normal error handling in ProduceEnd.
                // https://github.com/aspnet/KestrelHttpServer/issues/43
                if (!HasResponseStarted && _applicationException == null && _onStarting != null)
                {
                    await FireOnStarting();
                }

                PauseStreams();

                if (badRequestException == null)
                {
                    // If _requestAbort is set, the connection has already been closed.
                    if (_requestAborted == 0)
                    {
                        // Call ProduceEnd() before consuming the rest of the request body to prevent
                        // delaying clients waiting for the chunk terminator:
                        //
                        // https://github.com/dotnet/corefx/issues/17330#issuecomment-288248663
                        //
                        // This also prevents the 100 Continue response from being sent if the app
                        // never tried to read the body.
                        // https://github.com/aspnet/KestrelHttpServer/issues/2102
                        //
                        // ProduceEnd() must be called before _application.DisposeContext(), to ensure
                        // HttpContext.Response.StatusCode is correctly set when
                        // IHttpContextFactory.Dispose(HttpContext) is called.
                        await ProduceEnd();

                        // ForZeroContentLength does not complete the reader nor the writer
                        if (!messageBody.IsEmpty)
                        {
                            try
                            {
                                // Finish reading the request body in case the app did not.
                                await messageBody.ConsumeAsync();
                            }
                            catch (BadHttpRequestException ex)
                            {
                                // Capture BadHttpRequestException for further processing
                                badRequestException = ex;
                            }
                        }
                    }
                    else if (!HasResponseStarted)
                    {
                        // If the request was aborted and no response was sent, there's no
                        // meaningful status code to log.
                        StatusCode = 0;
                    }
                }

                if (_onCompleted != null)
                {
                    await FireOnCompleted();
                }

                if (badRequestException != null)
                {
                    // Handle BadHttpRequestException thrown during app execution or remaining message body consumption.
                    // This has to be caught here so StatusCode is set properly before disposing the HttpContext
                    // (DisposeContext logs StatusCode).
                    SetBadRequestState(badRequestException);
                }

                application.DisposeContext(httpContext, _applicationException);

                // StopStreams should be called before the end of the "if (!_requestProcessingStopping)" block
                // to ensure InitializeStreams has been called.
                StopStreams();

                if (HasStartedConsumingRequestBody)
                {
                    RequestBodyPipe.Reader.Complete();

                    // Wait for MessageBody.PumpAsync() to call RequestBodyPipe.Writer.Complete().
                    await messageBody.StopAsync();

                    // At this point both the request body pipe reader and writer should be completed.
                    RequestBodyPipe.Reset();
                }

                if (badRequestException != null)
                {
                    // Bad request reported, stop processing requests
                    return;
                }

            } while (_keepAlive);
        }

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

        protected Task FireOnStarting()
        {
            Stack<KeyValuePair<Func<object, Task>, object>> onStarting;
            lock (_onStartingSync)
            {
                onStarting = _onStarting;
                _onStarting = null;
            }

            if (onStarting == null)
            {
                return Task.CompletedTask;
            }
            else
            {
                return FireOnStartingMayAwait(onStarting);
            }

        }

        private Task FireOnStartingMayAwait(Stack<KeyValuePair<Func<object, Task>, object>> onStarting)
        {
            try
            {
                var count = onStarting.Count;
                for (var i = 0; i < count; i++)
                {
                    var entry = onStarting.Pop();
                    var task = entry.Key.Invoke(entry.Value);
                    if (!ReferenceEquals(task, Task.CompletedTask))
                    {
                        return FireOnStartingAwaited(task, onStarting);
                    }
                }
            }
            catch (Exception ex)
            {
                ReportApplicationError(ex);
            }

            return Task.CompletedTask;
        }

        private async Task FireOnStartingAwaited(Task currentTask, Stack<KeyValuePair<Func<object, Task>, object>> onStarting)
        {
            try
            {
                await currentTask;

                var count = onStarting.Count;
                for (var i = 0; i < count; i++)
                {
                    var entry = onStarting.Pop();
                    await entry.Key.Invoke(entry.Value);
                }
            }
            catch (Exception ex)
            {
                ReportApplicationError(ex);
            }
        }

        protected Task FireOnCompleted()
        {
            Stack<KeyValuePair<Func<object, Task>, object>> onCompleted;
            lock (_onCompletedSync)
            {
                onCompleted = _onCompleted;
                _onCompleted = null;
            }

            if (onCompleted == null)
            {
                return Task.CompletedTask;
            }

            return FireOnCompletedAwaited(onCompleted);
        }

        private async Task FireOnCompletedAwaited(Stack<KeyValuePair<Func<object, Task>, object>> onCompleted)
        {
            foreach (var entry in onCompleted)
            {
                try
                {
                    await entry.Key.Invoke(entry.Value);
                }
                catch (Exception ex)
                {
                    Log.ApplicationError(ConnectionId, TraceIdentifier, ex);
                }
            }
        }

        public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!HasResponseStarted)
            {
                var initializeTask = InitializeResponseAsync(0);
                // If return is Task.CompletedTask no awaiting is required
                if (!ReferenceEquals(initializeTask, Task.CompletedTask))
                {
                    return FlushAsyncAwaited(initializeTask, cancellationToken);
                }
            }

            return Output.FlushAsync(cancellationToken);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task FlushAsyncAwaited(Task initializeTask, CancellationToken cancellationToken)
        {
            await initializeTask;
            await Output.FlushAsync(cancellationToken);
        }

        public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default(CancellationToken))
        {
            // For the first write, ensure headers are flushed if WriteDataAsync isn't called.
            var firstWrite = !HasResponseStarted;

            if (firstWrite)
            {
                var initializeTask = InitializeResponseAsync(data.Length);
                // If return is Task.CompletedTask no awaiting is required
                if (!ReferenceEquals(initializeTask, Task.CompletedTask))
                {
                    return WriteAsyncAwaited(initializeTask, data, cancellationToken);
                }
            }
            else
            {
                VerifyAndUpdateWrite(data.Length);
            }

            if (_canHaveBody)
            {
                if (_autoChunk)
                {
                    if (data.Length == 0)
                    {
                        return !firstWrite ? Task.CompletedTask : FlushAsync(cancellationToken);
                    }
                    return WriteChunkedAsync(data, cancellationToken);
                }
                else
                {
                    CheckLastWrite();
                    return Output.WriteDataAsync(data.Span, cancellationToken: cancellationToken);
                }
            }
            else
            {
                HandleNonBodyResponseWrite();
                return !firstWrite ? Task.CompletedTask : FlushAsync(cancellationToken);
            }
        }

        public async Task WriteAsyncAwaited(Task initializeTask, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            await initializeTask;

            // WriteAsyncAwaited is only called for the first write to the body.
            // Ensure headers are flushed if Write(Chunked)Async isn't called.
            if (_canHaveBody)
            {
                if (_autoChunk)
                {
                    if (data.Length == 0)
                    {
                        await FlushAsync(cancellationToken);
                        return;
                    }

                    await WriteChunkedAsync(data, cancellationToken);
                }
                else
                {
                    CheckLastWrite();
                    await Output.WriteDataAsync(data.Span, cancellationToken: cancellationToken);
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
            var responseHeaders = HttpResponseHeaders;

            if (responseHeaders != null &&
                !responseHeaders.HasTransferEncoding &&
                responseHeaders.ContentLength.HasValue &&
                _responseBytesWritten + count > responseHeaders.ContentLength.Value)
            {
                _keepAlive = false;
                ThrowTooManyBytesWritten(count);
            }

            _responseBytesWritten += count;
        }

        [StackTraceHidden]
        private void ThrowTooManyBytesWritten(int count)
        {
            throw GetTooManyBytesWrittenException(count);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InvalidOperationException GetTooManyBytesWrittenException(int count)
        {
            var responseHeaders = HttpResponseHeaders;
            return new InvalidOperationException(
                CoreStrings.FormatTooManyBytesWritten(_responseBytesWritten + count, responseHeaders.ContentLength.Value));
        }

        private void CheckLastWrite()
        {
            var responseHeaders = HttpResponseHeaders;

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
            var responseHeaders = HttpResponseHeaders;

            if (Method != HttpMethod.Head &&
                StatusCode != StatusCodes.Status304NotModified &&
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
                    CoreStrings.FormatTooFewBytesWritten(_responseBytesWritten, responseHeaders.ContentLength.Value)));
            }
        }

        private Task WriteChunkedAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            return Output.WriteAsync(_writeChunk, data);
        }

        private static void WriteChunk(PipeWriter writableBuffer, ReadOnlyMemory<byte> buffer)
        {
            var writer = new BufferWriter<PipeWriter>(writableBuffer);
            if (buffer.Length > 0)
            {
                ChunkWriter.WriteBeginChunkBytes(ref writer, buffer.Length);
                writer.Write(buffer.Span);
                ChunkWriter.WriteEndChunkBytes(ref writer);
                writer.Commit();
            }
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

            if (_httpVersion != Http.HttpVersion.Http10 &&
                RequestHeaders.TryGetValue("Expect", out var expect) &&
                (expect.FirstOrDefault() ?? "").Equals("100-continue", StringComparison.OrdinalIgnoreCase))
            {
                Output.Write100ContinueAsync(default(CancellationToken)).GetAwaiter().GetResult();
            }
        }

        public Task InitializeResponseAsync(int firstWriteByteCount)
        {
            var startingTask = FireOnStarting();
            // If return is Task.CompletedTask no awaiting is required
            if (!ReferenceEquals(startingTask, Task.CompletedTask))
            {
                return InitializeResponseAwaited(startingTask, firstWriteByteCount);
            }

            if (_applicationException != null)
            {
                ThrowResponseAbortedException();
            }

            VerifyAndUpdateWrite(firstWriteByteCount);
            ProduceStart(appCompleted: false);

            return Task.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task InitializeResponseAwaited(Task startingTask, int firstWriteByteCount)
        {
            await startingTask;

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

            CreateResponseHeader(appCompleted);
        }

        protected Task TryProduceInvalidRequestResponse()
        {
            // If _requestAborted is set, the connection has already been closed.
            if (_requestRejectedException != null && _requestAborted == 0)
            {
                return ProduceEnd();
            }

            return Task.CompletedTask;
        }

        protected Task ProduceEnd()
        {
            if (_requestRejectedException != null || _applicationException != null)
            {
                if (HasResponseStarted)
                {
                    // We can no longer change the response, so we simply close the connection.
                    _keepAlive = false;
                    return Task.CompletedTask;
                }

                // If the request was rejected, the error state has already been set by SetBadRequestState and
                // that should take precedence.
                if (_requestRejectedException != null)
                {
                    SetErrorResponseException(_requestRejectedException);
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task ProduceEndAwaited()
        {
            ProduceStart(appCompleted: true);

            // Force flush
            await Output.FlushAsync(default(CancellationToken));

            await WriteSuffix();
        }

        private Task WriteSuffix()
        {
            // _autoChunk should be checked after we are sure ProduceStart() has been called
            // since ProduceStart() may set _autoChunk to true.
            if (_autoChunk || _httpVersion == Http.HttpVersion.Http2)
            {
                return WriteSuffixAwaited();
            }

            if (_keepAlive)
            {
                Log.ConnectionKeepAlive(ConnectionId);
            }

            if (Method == HttpMethod.Head && _responseBytesWritten > 0)
            {
                Log.ConnectionHeadResponseBodyWrite(ConnectionId, _responseBytesWritten);
            }

            return Task.CompletedTask;
        }

        private async Task WriteSuffixAwaited()
        {
            // For the same reason we call CheckLastWrite() in Content-Length responses.
            _abortedCts = null;

            await Output.WriteStreamSuffixAsync(default(CancellationToken));

            if (_keepAlive)
            {
                Log.ConnectionKeepAlive(ConnectionId);
            }

            if (Method == HttpMethod.Head && _responseBytesWritten > 0)
            {
                Log.ConnectionHeadResponseBodyWrite(ConnectionId, _responseBytesWritten);
            }
        }

        private void CreateResponseHeader(bool appCompleted)
        {
            var responseHeaders = HttpResponseHeaders;
            var hasConnection = responseHeaders.HasConnection;
            var connectionOptions = HttpHeaders.ParseConnection(responseHeaders.HeaderConnection);
            var hasTransferEncoding = responseHeaders.HasTransferEncoding;
            var transferCoding = HttpHeaders.GetFinalTransferCoding(responseHeaders.HeaderTransferEncoding);

            if (_keepAlive && hasConnection && (connectionOptions & ConnectionOptions.KeepAlive) != ConnectionOptions.KeepAlive)
            {
                _keepAlive = false;
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
            _canHaveBody = StatusCanHaveBody(StatusCode) && Method != HttpMethod.Head;

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
                        //
                        // This also covers HTTP/2, which forbids chunked encoding in RFC 7540 (section 8.1:
                        //
                        // The chunked transfer encoding defined in Section 4.1 of [RFC7230] MUST NOT be used in HTTP/2.
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

            if (!hasConnection && _httpVersion != Http.HttpVersion.Http2)
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

            Output.WriteResponseHeaders(StatusCode, ReasonPhrase, responseHeaders);
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
            throw new InvalidOperationException(CoreStrings.FormatParameterReadOnlyAfterResponseStarted(value));
        }

        private void RejectNonBodyTransferEncodingResponse(bool appCompleted)
        {
            var ex = new InvalidOperationException(CoreStrings.FormatHeaderNotAllowedOnResponse("Transfer-Encoding", StatusCode));
            if (!appCompleted)
            {
                // Back out of header creation surface exeception in user code
                _requestProcessingStatus = RequestProcessingStatus.AppStarted;
                throw ex;
            }
            else
            {
                ReportApplicationError(ex);

                // 500 Internal Server Error
                SetErrorResponseHeaders(statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        private void SetErrorResponseException(BadHttpRequestException ex)
        {
            SetErrorResponseHeaders(ex.StatusCode);

            if (!StringValues.IsNullOrEmpty(ex.AllowedHeader))
            {
                HttpResponseHeaders.HeaderAllow = ex.AllowedHeader;
            }
        }

        private void SetErrorResponseHeaders(int statusCode)
        {
            Debug.Assert(!HasResponseStarted, $"{nameof(SetErrorResponseHeaders)} called after response had already started.");

            StatusCode = statusCode;
            ReasonPhrase = null;

            var responseHeaders = HttpResponseHeaders;
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
            if (Method != HttpMethod.Head)
            {
                ThrowWritingToResponseBodyNotSupported();
            }
        }

        [StackTraceHidden]
        private void ThrowWritingToResponseBodyNotSupported()
        {
            // Throw Exception for 204, 205, 304 responses.
            throw new InvalidOperationException(CoreStrings.FormatWritingToResponseBodyNotSupported(StatusCode));
        }

        [StackTraceHidden]
        private void ThrowResponseAbortedException()
        {
            throw new ObjectDisposedException(CoreStrings.UnhandledApplicationException, _applicationException);
        }

        [StackTraceHidden]
        public void ThrowRequestTargetRejected(Span<byte> target)
            => throw GetInvalidRequestTargetException(target);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private BadHttpRequestException GetInvalidRequestTargetException(Span<byte> target)
            => BadHttpRequestException.GetException(
                RequestRejectionReason.InvalidRequestTarget,
                Log.IsEnabled(LogLevel.Information)
                    ? target.GetAsciiStringEscaped(Constants.MaxExceptionDetailSize)
                    : string.Empty);

        public void SetBadRequestState(RequestRejectionReason reason)
        {
            SetBadRequestState(BadHttpRequestException.GetException(reason));
        }

        public void SetBadRequestState(BadHttpRequestException ex)
        {
            Log.ConnectionBadRequest(ConnectionId, ex);

            if (!HasResponseStarted)
            {
                SetErrorResponseException(ex);
            }

            _keepAlive = false;
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

            Log.ApplicationError(ConnectionId, TraceIdentifier, ex);
        }

        private Pipe CreateRequestBodyPipe()
            => new Pipe(new PipeOptions
            (
                pool: _context.MemoryPool,
                readerScheduler: ServiceContext.Scheduler,
                writerScheduler: PipeScheduler.Inline,
                pauseWriterThreshold: 1,
                resumeWriterThreshold: 1,
                useSynchronizationContext: false,
                minimumSegmentSize: KestrelMemoryPool.MinimumSegmentSize
            ));
    }
}
