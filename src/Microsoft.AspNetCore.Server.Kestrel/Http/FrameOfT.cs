// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    public class Frame<TContext> : Frame
    {
        private readonly IHttpApplication<TContext> _application;

        public Frame(IHttpApplication<TContext> application,
                     ConnectionContext context)
            : this(application, context, remoteEndPoint: null, localEndPoint: null, prepareRequest: null)
        {
        }

        public Frame(IHttpApplication<TContext> application,
                     ConnectionContext context,
                     IPEndPoint remoteEndPoint,
                     IPEndPoint localEndPoint,
                     Action<IFeatureCollection> prepareRequest)
            : base(context, remoteEndPoint, localEndPoint, prepareRequest)
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
            try
            {
                while (_frameState.CurrentState == RequestState.Waiting)
                {
                    while (_frameState.CurrentState < RequestState.Stopping && !TakeStartLine(SocketInput))
                    {
                        if (SocketInput.RemoteIntakeFin)
                        {
                            return;
                        }
                        await SocketInput;
                    }

                    while (_frameState.CurrentState < RequestState.Stopping && !TakeMessageHeaders(SocketInput, _requestHeaders))
                    {
                        if (SocketInput.RemoteIntakeFin)
                        {
                            return;
                        }
                        await SocketInput;
                    }

                    if (_frameState.TransitionToState(RequestState.ExecutingRequest) == RequestState.ExecutingRequest)
                    {
                        var messageBody = MessageBody.For(HttpVersion, _requestHeaders, this);
                        _keepAlive = messageBody.RequestKeepAlive;

                        // _duplexStream may be null if flag switched while running
                        if (!Settings.ReuseStreams || _duplexStream == null)
                        {
                            _requestBody = new FrameRequestStream();
                            _responseBody = new FrameResponseStream(this);
                            _duplexStream = new FrameDuplexStream(_requestBody, _responseBody);
                        }

                        RequestBody = _requestBody.StartAcceptingReads(messageBody);
                        ResponseBody = _responseBody.StartAcceptingWrites();
                        DuplexStream = _duplexStream;

                        _abortedCts = null;
                        _manuallySetRequestAbortToken = null;

                        var context = _application.CreateContext(this);
                        try
                        {
                            await _application.ProcessRequestAsync(context).ConfigureAwait(false);
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
                            if (!_responseStarted && _applicationException == null && _onStarting != null)
                            {
                                await FireOnStarting();
                            }

                            _requestBody.PauseAcceptingReads();
                            _responseBody.PauseAcceptingWrites();

                            if (_onCompleted != null)
                            {
                                await FireOnCompleted();
                            }

                            _application.DisposeContext(context, _applicationException);

                            // If Aborted, the connection has already been closed.
                            if (_frameState.CurrentState != RequestState.Aborted)
                            {
                                _responseBody.ResumeAcceptingWrites();
                                await ProduceEnd();

                                if (_keepAlive)
                                {
                                    _requestBody.ResumeAcceptingReads();
                                    // Finish reading the request body in case the app did not.
                                    await messageBody.Consume();
                                }
                            }

                            _requestBody.StopAcceptingReads();
                            _responseBody.StopAcceptingWrites();
                        }

                        if (!_keepAlive)
                        {
                            return;
                        }
                    }

                    if (!Reset())
                    {
                        return;
                    }
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
                    var frameState = _frameState.CurrentState;
                    _frameState.Dispose();
                    _abortedCts = null;

                    // If Aborted, the connection has already been closed.
                    if (frameState != RequestState.Aborted)
                    {
                        // Inform client no more data will ever arrive
                        ConnectionControl.End(ProduceEndType.SocketShutdownSend);

                        // Wait for client to either disconnect or send unexpected data
                        await SocketInput;

                        // Dispose socket
                        ConnectionControl.End(ProduceEndType.SocketDisconnect);
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
