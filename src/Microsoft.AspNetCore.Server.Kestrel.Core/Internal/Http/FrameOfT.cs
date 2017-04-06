// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public class Frame<TContext> : Frame
    {
        private readonly IHttpApplication<TContext> _application;

        public Frame(IHttpApplication<TContext> application, FrameContext frameContext)
            : base(frameContext)
        {
            _application = application;
        }

        /// <summary>
        /// Primary loop which consumes socket input, parses it for protocol framing, and invokes the
        /// application delegate for as long as the socket is intended to remain open.
        /// The resulting Task from this loop is preserved in a field which is used when the server needs
        /// to drain and close all currently active connections.
        /// </summary>
        public override async Task RequestProcessingAsync()
        {
            var result = default(ReadResult);
            var buffer = default(ReadableBuffer);
            var examined = default(ReadCursor);
            var consumed = default(ReadCursor);
            var needBuffer = true;

            try
            {
                while (!_requestProcessingStopping)
                {
                    TimeoutControl.SetTimeout(_keepAliveMilliseconds, TimeoutAction.CloseConnection);

                    InitializeHeaders();

                    while (!_requestProcessingStopping)
                    {
                        if (needBuffer)
                        {
                            result = await Input.ReadAsync();
                            buffer = result.Buffer;
                            needBuffer = false;
                        }

                        var needAdvance = false;

                        try
                        {
                            ParseRequest(ref buffer, out consumed, out examined);

                            if (buffer.Length == 0)
                            {
                                // We've consumed the entire buffer so we need to advance the pipe
                                needAdvance = true;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // An exception occured just advance the buffer
                            needAdvance = true;

                            if (_requestProcessingStatus == RequestProcessingStatus.ParsingHeaders)
                            {
                                throw BadHttpRequestException.GetException(RequestRejectionReason
                                    .MalformedRequestInvalidHeaders);
                            }

                            throw;
                        }
                        finally
                        {
                            if (needAdvance)
                            {
                                Input.Advance(consumed, examined);

                                // We also need to request more data from the pipe
                                needBuffer = true;
                            }
                        }

                        if (_requestProcessingStatus == RequestProcessingStatus.AppStarted)
                        {
                            break;
                        }

                        if (result.IsCompleted)
                        {
                            switch (_requestProcessingStatus)
                            {
                                case RequestProcessingStatus.RequestPending:
                                    return;
                                case RequestProcessingStatus.ParsingRequestLine:
                                    throw BadHttpRequestException.GetException(
                                        RequestRejectionReason.InvalidRequestLine);
                                case RequestProcessingStatus.ParsingHeaders:
                                    throw BadHttpRequestException.GetException(RequestRejectionReason
                                        .MalformedRequestInvalidHeaders);
                            }
                        }

                        // Incomplete message, we need a new buffer from the pipe
                        Input.Advance(consumed, examined);

                        needBuffer = true;
                    }

                    if (!_requestProcessingStopping)
                    {
                        var messageBody = MessageBody.For(_httpVersion, FrameRequestHeaders, this);
                        _keepAlive = messageBody.RequestKeepAlive;
                        _upgrade = messageBody.RequestUpgrade;

                        if (!needBuffer && !messageBody.IsEmpty)
                        {
                            // If there's a message body then we need to read from the pipe on the next request
                            // because our buffer is probably invalid
                            needBuffer = true;

                            // We need to advance here since we may not have advanced before this call
                            Input.Advance(consumed, examined);
                        }

                        InitializeStreams(messageBody);

                        var context = _application.CreateContext(this);
                        try
                        {
                            try
                            {
                                KestrelEventSource.Log.RequestStart(this);

                                await _application.ProcessRequestAsync(context).ConfigureAwait(false);

                                if (Volatile.Read(ref _requestAborted) == 0)
                                {
                                    VerifyResponseContentLength();
                                }
                            }
                            catch (Exception ex)
                            {
                                ReportApplicationError(ex);

                                if (ex is BadHttpRequestException)
                                {
                                    throw;
                                }
                            }
                            finally
                            {
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

                                if (_onCompleted != null)
                                {
                                    await FireOnCompleted();
                                }
                            }

                            // If _requestAbort is set, the connection has already been closed.
                            if (Volatile.Read(ref _requestAborted) == 0)
                            {
                                ResumeStreams();

                                if (HasResponseStarted)
                                {
                                    // If the response has already started, call ProduceEnd() before
                                    // consuming the rest of the request body to prevent
                                    // delaying clients waiting for the chunk terminator:
                                    //
                                    // https://github.com/dotnet/corefx/issues/17330#issuecomment-288248663
                                    //
                                    // ProduceEnd() must be called before _application.DisposeContext(), to ensure
                                    // HttpContext.Response.StatusCode is correctly set when
                                    // IHttpContextFactory.Dispose(HttpContext) is called.
                                    await ProduceEnd();
                                }

                                if (_keepAlive)
                                {
                                    // Finish reading the request body in case the app did not.
                                    await messageBody.Consume();
                                }

                                if (!HasResponseStarted)
                                {
                                    await ProduceEnd();
                                }
                            }
                            else if (!HasResponseStarted)
                            {
                                // If the request was aborted and no response was sent, there's no
                                // meaningful status code to log.
                                StatusCode = 0;
                            }
                        }
                        catch (BadHttpRequestException ex)
                        {
                            // Handle BadHttpRequestException thrown during app execution or remaining message body consumption.
                            // This has to be caught here so StatusCode is set properly before disposing the HttpContext
                            // (DisposeContext logs StatusCode).
                            SetBadRequestState(ex);
                        }
                        finally
                        {
                            _application.DisposeContext(context, _applicationException);

                            // StopStreams should be called before the end of the "if (!_requestProcessingStopping)" block
                            // to ensure InitializeStreams has been called.
                            StopStreams();
                        }
                    }

                    if (!_keepAlive)
                    {
                        // End the connection for non keep alive as data incoming may have been thrown off
                        return;
                    }

                    // Don't reset frame state if we're exiting the loop. This avoids losing request rejection
                    // information (for 4xx response), and prevents ObjectDisposedException on HTTPS (ODEs
                    // will be thrown if PrepareRequest is not null and references objects disposed on connection
                    // close - see https://github.com/aspnet/KestrelHttpServer/issues/1103#issuecomment-250237677).
                    if (!_requestProcessingStopping)
                    {
                        Reset();
                    }
                }
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
                Log.LogWarning(0, ex, "Connection processing ended abnormally");
            }
            finally
            {
                try
                {
                    // If we left a pending read hanging, the advance the pipe
                    if (!needBuffer)
                    {
                        Input.Advance(consumed, examined);
                    }

                    Input.Complete();
                    // If _requestAborted is set, the connection has already been closed.
                    if (Volatile.Read(ref _requestAborted) == 0)
                    {
                        await TryProduceInvalidRequestResponse();
                        LifetimeControl.End(ProduceEndType.SocketShutdown);
                    }
                }
                catch (Exception ex)
                {
                    Log.LogWarning(0, ex, "Connection shutdown abnormally");
                }
            }
        }
    }
}
