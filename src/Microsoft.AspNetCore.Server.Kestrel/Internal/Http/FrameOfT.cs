// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class Frame<TContext> : Frame
    {
        private readonly IHttpApplication<TContext> _application;

        public Frame(IHttpApplication<TContext> application,
                     ConnectionContext context)
            : base(context)
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
            var requestLineStatus = default(RequestLineStatus);

            try
            {
                while (!_requestProcessingStopping)
                {
                    // If writer completes with an error Input.ReadAsyncDispatched would throw and
                    // this would not be reset to empty. But it's required by ECONNRESET check lower in the method.
                    requestLineStatus = RequestLineStatus.Empty;

                    ConnectionControl.SetTimeout(_keepAliveMilliseconds, TimeoutAction.CloseConnection);

                    while (!_requestProcessingStopping)
                    {
                        var result = await Input.Reader.ReadAsync();
                        var examined = result.Buffer.End;
                        var consumed = result.Buffer.End;

                        try
                        {
                            if (!result.Buffer.IsEmpty)
                            {
                                requestLineStatus = TakeStartLine(result.Buffer, out consumed, out examined)
                                    ? RequestLineStatus.Done : RequestLineStatus.Incomplete;
                            }
                            else
                            {
                                requestLineStatus = RequestLineStatus.Empty;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            throw BadHttpRequestException.GetException(RequestRejectionReason.InvalidRequestLine);
                        }
                        finally
                        {
                            Input.Reader.Advance(consumed, examined);
                        }

                        if (requestLineStatus == RequestLineStatus.Done)
                        {
                            break;
                        }

                        if (result.IsCompleted)
                        {
                            if (requestLineStatus == RequestLineStatus.Empty)
                            {
                                return;
                            }

                            RejectRequest(RequestRejectionReason.InvalidRequestLine, requestLineStatus.ToString());
                        }
                    }

                    InitializeHeaders();

                    while (!_requestProcessingStopping)
                    {

                        var result = await Input.Reader.ReadAsync();
                        var examined = result.Buffer.End;
                        var consumed = result.Buffer.End;

                        bool headersDone;

                        try
                        {
                            headersDone = TakeMessageHeaders(result.Buffer, FrameRequestHeaders, out consumed,
                                out examined);
                        }
                        catch (InvalidOperationException)
                        {
                            throw BadHttpRequestException.GetException(RequestRejectionReason.MalformedRequestInvalidHeaders);
                        }
                        finally
                        {
                            Input.Reader.Advance(consumed, examined);
                        }

                        if (headersDone)
                        {
                            break;
                        }

                        if (result.IsCompleted)
                        {
                            RejectRequest(RequestRejectionReason.MalformedRequestInvalidHeaders);
                        }
                    }

                    if (!_requestProcessingStopping)
                    {
                        var messageBody = MessageBody.For(_httpVersion, FrameRequestHeaders, this);
                        _keepAlive = messageBody.RequestKeepAlive;
                        _upgrade = messageBody.RequestUpgrade;

                        InitializeStreams(messageBody);

                        var context = _application.CreateContext(this);
                        try
                        {
                            try
                            {
                                await _application.ProcessRequestAsync(context).ConfigureAwait(false);
                                VerifyResponseContentLength();
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

                                if (_keepAlive)
                                {
                                    // Finish reading the request body in case the app did not.
                                    await messageBody.Consume();
                                }

                                // ProduceEnd() must be called before _application.DisposeContext(), to ensure
                                // HttpContext.Response.StatusCode is correctly set when
                                // IHttpContextFactory.Dispose(HttpContext) is called.
                                await ProduceEnd();
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
            catch (IOException ex) when (ex.InnerException is UvException)
            {
                // Don't log ECONNRESET errors made between requests. Browsers like IE will reset connections regularly.
                if (requestLineStatus != RequestLineStatus.Empty ||
                    ((UvException)ex.InnerException).StatusCode != Constants.ECONNRESET)
                {
                    Log.RequestProcessingError(ConnectionId, ex);
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning(0, ex, "Connection processing ended abnormally");
            }
            finally
            {
                try
                {
                    Input.Reader.Complete();
                    // If _requestAborted is set, the connection has already been closed.
                    if (Volatile.Read(ref _requestAborted) == 0)
                    {
                        await TryProduceInvalidRequestResponse();
                        ConnectionControl.End(ProduceEndType.SocketShutdown);
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
