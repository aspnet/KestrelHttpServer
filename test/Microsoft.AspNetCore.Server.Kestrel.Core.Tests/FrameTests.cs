// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class FrameTests : IDisposable
    {
        private readonly IPipe _input;
        private readonly TestFrame<object> _frame;
        private readonly ServiceContext _serviceContext;
        private readonly FrameContext _frameContext;
        private readonly PipeFactory _pipelineFactory;
        private ReadCursor _consumed;
        private ReadCursor _examined;
        private Mock<ITimeoutControl> _timeoutControl;

        private class TestFrame<TContext> : Frame<TContext>
        {
            public TestFrame(IHttpApplication<TContext> application, FrameContext context)
                : base(application, context)
            {
            }

            public Task ProduceEndAsync()
            {
                return ProduceEnd();
            }
        }

        public FrameTests()
        {
            _pipelineFactory = new PipeFactory();
            _input = _pipelineFactory.Create();
            var output = _pipelineFactory.Create();

            _serviceContext = new TestServiceContext();
            _timeoutControl = new Mock<ITimeoutControl>();
            _frameContext = new FrameContext
            {
                ServiceContext = _serviceContext,
                ConnectionInformation = new MockConnectionInformation
                {
                    PipeFactory = _pipelineFactory
                },
                TimeoutControl = _timeoutControl.Object,
                Input = _input.Reader,
                Output = output
            };

            _frame = new TestFrame<object>(application: null, context: _frameContext);
            _frame.Reset();
        }

        public void Dispose()
        {
            _input.Reader.Complete();
            _input.Writer.Complete();
            _pipelineFactory.Dispose();
        }

        [Fact]
        public async Task TakeMessageHeadersThrowsWhenHeadersExceedTotalSizeLimit()
        {
            const string headerLine = "Header: value\r\n";
            _serviceContext.ServerOptions.Limits.MaxRequestHeadersTotalSize = headerLine.Length - 1;
            _frame.Reset();

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"{headerLine}\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() => _frame.TakeMessageHeaders(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal(CoreStrings.BadRequest_HeadersExceedMaxTotalSize, exception.Message);
            Assert.Equal(StatusCodes.Status431RequestHeaderFieldsTooLarge, exception.StatusCode);
        }

        [Fact]
        public async Task TakeMessageHeadersThrowsWhenHeadersExceedCountLimit()
        {
            const string headerLines = "Header-1: value1\r\nHeader-2: value2\r\n";
            _serviceContext.ServerOptions.Limits.MaxRequestHeaderCount = 1;

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"{headerLines}\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() => _frame.TakeMessageHeaders(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal(CoreStrings.BadRequest_TooManyHeaders, exception.Message);
            Assert.Equal(StatusCodes.Status431RequestHeaderFieldsTooLarge, exception.StatusCode);
        }

        [Fact]
        public void ResetResetsScheme()
        {
            _frame.Scheme = "https";

            // Act
            _frame.Reset();

            // Assert
            Assert.Equal("http", ((IFeatureCollection)_frame).Get<IHttpRequestFeature>().Scheme);
        }

        [Fact]
        public void ResetResetsTraceIdentifier()
        {
            _frame.TraceIdentifier = "xyz";

            _frame.Reset();

            var nextId = ((IFeatureCollection)_frame).Get<IHttpRequestIdentifierFeature>().TraceIdentifier;
            Assert.NotEqual("xyz", nextId);

            _frame.Reset();
            var secondId = ((IFeatureCollection)_frame).Get<IHttpRequestIdentifierFeature>().TraceIdentifier;
            Assert.NotEqual(nextId, secondId);
        }

        [Fact]
        public void ResetResetsMinRequestBodyDataRate()
        {
            _frame.MinRequestBodyDataRate = new MinDataRate(bytesPerSecond: 1, gracePeriod: TimeSpan.MaxValue);

            _frame.Reset();

            Assert.Same(_serviceContext.ServerOptions.Limits.MinRequestBodyDataRate, _frame.MinRequestBodyDataRate);
        }

        [Fact]
        public void ResetResetsMinResponseDataRate()
        {
            _frame.MinResponseDataRate = new MinDataRate(bytesPerSecond: 1, gracePeriod: TimeSpan.MaxValue);

            _frame.Reset();

            Assert.Same(_serviceContext.ServerOptions.Limits.MinResponseDataRate, _frame.MinResponseDataRate);
        }

        [Fact]
        public void TraceIdentifierCountsRequestsPerFrame()
        {
            var connectionId = _frameContext.ConnectionId;
            var feature = ((IFeatureCollection)_frame).Get<IHttpRequestIdentifierFeature>();
            // Reset() is called once in the test ctor
            var count = 1;
            void Reset()
            {
                _frame.Reset();
                count++;
            }

            var nextId = feature.TraceIdentifier;
            Assert.Equal($"{connectionId}:00000001", nextId);

            Reset();
            var secondId = feature.TraceIdentifier;
            Assert.Equal($"{connectionId}:00000002", secondId);

            var big = 1_000_000;
            while (big-- > 0) Reset();
            Assert.Equal($"{connectionId}:{count:X8}", feature.TraceIdentifier);
        }

        [Fact]
        public void TraceIdentifierGeneratesWhenNull()
        {
            _frame.TraceIdentifier = null;
            var id = _frame.TraceIdentifier;
            Assert.NotNull(id);
            Assert.Equal(id, _frame.TraceIdentifier);

            _frame.Reset();
            Assert.NotEqual(id, _frame.TraceIdentifier);
        }

        [Fact]
        public async Task ResetResetsHeaderLimits()
        {
            const string headerLine1 = "Header-1: value1\r\n";
            const string headerLine2 = "Header-2: value2\r\n";

            var options = new KestrelServerOptions();
            options.Limits.MaxRequestHeadersTotalSize = headerLine1.Length;
            options.Limits.MaxRequestHeaderCount = 1;
            _serviceContext.ServerOptions = options;

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"{headerLine1}\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var takeMessageHeaders = _frame.TakeMessageHeaders(readableBuffer, out _consumed, out _examined);
            _input.Reader.Advance(_consumed, _examined);

            Assert.True(takeMessageHeaders);
            Assert.Equal(1, _frame.RequestHeaders.Count);
            Assert.Equal("value1", _frame.RequestHeaders["Header-1"]);

            _frame.Reset();

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"{headerLine2}\r\n"));
            readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            takeMessageHeaders = _frame.TakeMessageHeaders(readableBuffer, out _consumed, out _examined);
            _input.Reader.Advance(_consumed, _examined);

            Assert.True(takeMessageHeaders);
            Assert.Equal(1, _frame.RequestHeaders.Count);
            Assert.Equal("value2", _frame.RequestHeaders["Header-2"]);
        }

        [Fact]
        public async Task ThrowsWhenStatusCodeIsSetAfterResponseStarted()
        {
            // Act
            await _frame.WriteAsync(new ArraySegment<byte>(new byte[1]));

            // Assert
            Assert.True(_frame.HasResponseStarted);
            Assert.Throws<InvalidOperationException>(() => ((IHttpResponseFeature)_frame).StatusCode = StatusCodes.Status404NotFound);
        }

        [Fact]
        public async Task ThrowsWhenReasonPhraseIsSetAfterResponseStarted()
        {
            // Act
            await _frame.WriteAsync(new ArraySegment<byte>(new byte[1]));

            // Assert
            Assert.True(_frame.HasResponseStarted);
            Assert.Throws<InvalidOperationException>(() => ((IHttpResponseFeature)_frame).ReasonPhrase = "Reason phrase");
        }

        [Fact]
        public async Task ThrowsWhenOnStartingIsSetAfterResponseStarted()
        {
            await _frame.WriteAsync(new ArraySegment<byte>(new byte[1]));

            // Act/Assert
            Assert.True(_frame.HasResponseStarted);
            Assert.Throws<InvalidOperationException>(() => ((IHttpResponseFeature)_frame).OnStarting(_ => Task.CompletedTask, null));
        }

        [Theory]
        [MemberData(nameof(MinDataRateData))]
        public void ConfiguringIHttpMinRequestBodyDataRateFeatureSetsMinRequestBodyDataRate(MinDataRate minDataRate)
        {
            ((IFeatureCollection)_frame).Get<IHttpMinRequestBodyDataRateFeature>().MinDataRate = minDataRate;

            Assert.Same(minDataRate, _frame.MinRequestBodyDataRate);
        }

        [Theory]
        [MemberData(nameof(MinDataRateData))]
        public void ConfiguringIHttpMinResponseDataRateFeatureSetsMinResponseDataRate(MinDataRate minDataRate)
        {
            ((IFeatureCollection)_frame).Get<IHttpMinResponseDataRateFeature>().MinDataRate = minDataRate;

            Assert.Same(minDataRate, _frame.MinResponseDataRate);
        }

        [Fact]
        public void ResetResetsRequestHeaders()
        {
            // Arrange
            var originalRequestHeaders = _frame.RequestHeaders;
            _frame.RequestHeaders = new FrameRequestHeaders();

            // Act
            _frame.Reset();

            // Assert
            Assert.Same(originalRequestHeaders, _frame.RequestHeaders);
        }

        [Fact]
        public void ResetResetsResponseHeaders()
        {
            // Arrange
            var originalResponseHeaders = _frame.ResponseHeaders;
            _frame.ResponseHeaders = new FrameResponseHeaders();

            // Act
            _frame.Reset();

            // Assert
            Assert.Same(originalResponseHeaders, _frame.ResponseHeaders);
        }

        [Fact]
        public void InitializeStreamsResetsStreams()
        {
            // Arrange
            var messageBody = MessageBody.For(Kestrel.Core.Internal.Http.HttpVersion.Http11, (FrameRequestHeaders)_frame.RequestHeaders, _frame);
            _frame.InitializeStreams(messageBody);

            var originalRequestBody = _frame.RequestBody;
            var originalResponseBody = _frame.ResponseBody;
            _frame.RequestBody = new MemoryStream();
            _frame.ResponseBody = new MemoryStream();

            // Act
            _frame.InitializeStreams(messageBody);

            // Assert
            Assert.Same(originalRequestBody, _frame.RequestBody);
            Assert.Same(originalResponseBody, _frame.ResponseBody);
        }

        [Theory]
        [MemberData(nameof(RequestLineValidData))]
        public async Task TakeStartLineSetsFrameProperties(
            string requestLine,
            string expectedMethod,
            string expectedRawTarget,
            string expectedRawPath,
            string expectedDecodedPath,
            string expectedQueryString,
            string expectedHttpVersion)
        {
            var requestLineBytes = Encoding.ASCII.GetBytes(requestLine);
            await _input.Writer.WriteAsync(requestLineBytes);
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var returnValue = _frame.TakeStartLine(readableBuffer, out _consumed, out _examined);
            _input.Reader.Advance(_consumed, _examined);

            Assert.True(returnValue);
            Assert.Equal(expectedMethod, _frame.Method);
            Assert.Equal(expectedRawTarget, _frame.RawTarget);
            Assert.Equal(expectedDecodedPath, _frame.Path);
            Assert.Equal(expectedQueryString, _frame.QueryString);
            Assert.Equal(expectedHttpVersion, _frame.HttpVersion);
        }

        [Theory]
        [MemberData(nameof(RequestLineDotSegmentData))]
        public async Task TakeStartLineRemovesDotSegmentsFromTarget(
            string requestLine,
            string expectedRawTarget,
            string expectedDecodedPath,
            string expectedQueryString)
        {
            var requestLineBytes = Encoding.ASCII.GetBytes(requestLine);
            await _input.Writer.WriteAsync(requestLineBytes);
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var returnValue = _frame.TakeStartLine(readableBuffer, out _consumed, out _examined);
            _input.Reader.Advance(_consumed, _examined);

            Assert.True(returnValue);
            Assert.Equal(expectedRawTarget, _frame.RawTarget);
            Assert.Equal(expectedDecodedPath, _frame.Path);
            Assert.Equal(expectedQueryString, _frame.QueryString);
        }

        [Fact]
        public async Task ParseRequestStartsRequestHeadersTimeoutOnFirstByteAvailable()
        {
            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes("G"));

            _frame.ParseRequest((await _input.Reader.ReadAsync()).Buffer, out _consumed, out _examined);
            _input.Reader.Advance(_consumed, _examined);

            var expectedRequestHeadersTimeout = _serviceContext.ServerOptions.Limits.RequestHeadersTimeout.Ticks;
            _timeoutControl.Verify(cc => cc.ResetTimeout(expectedRequestHeadersTimeout, TimeoutAction.SendTimeoutResponse));
        }

        [Fact]
        public async Task ParseRequestOnBoundary()
        {
            // This is exactly 2 blocks in the pipe
            var bigRequest = "GET /api/testBaconCow/listByPorkChopCow?id_Bacon_Chop=6&skip=0&take=10 HTTP/1.1\r\nHost: localhost:5000\r\nConnection: keep-alive\r\nAccept: application/json, text/plain, */*\r\nX-XSRF-TOKEN: CfDJ8OFOJERUl99Io1MZi0D5PWrBM5ofPEr_0w-OUgwrsh1sUwRX4Oo2sqR2UMcvZypw0ppGAL-qiMl_rXZqtIorzdPc2nAJLxBCs92HgSPw0wK3EQFQoaLfiZoec5st2cNOuvsazHcWo19shMbTEgKthgbWCJz5pHyLZIdlwNCYmdFV-AITJ4xAjgsAk42lJ5aapw\r\nUser-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/62.0.3202.94 Safari/537.36\r\nContent-Type: application/json\r\nDNT: 1\r\nReferer: http://localhost:5000/Test\r\nAccept-Encoding: gzip, deflate, br\r\nAccept-Language: pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7\r\nCookie: .AspNetCore.Antiforgery.1nvm0JTClpg=CfDJ8OFOJERUl99Io1MZi0D5PWpDXcy7LqHVq4UJcnYwIRHeOF4Apr6_8roYTCrdh8PmjvcuZod9ezz9olQtjl1RVvPa8NIbA5XRlnzpplsPlfApgBLiN414VxuXQ4wmRNlDSxL_v5TIV4GjdEFIdJlbdPo; .AspNetCore.Identity.Application=chunks-2; .AspNetCore.Identity.ApplicationC1=CfDJ8OFOJERUl99Io1MZi0D5PWqI2d-EJe8X0e9nu7lhT4P57etmNjtFzgLMjNVri2b8jMROTBw41lC4vb3mGNSCesNT48cu0KsVvXda7UlzpM4XUnMmiUpIyTKfl2rXiAA2nHLpDnKa_yXgL7Mg6ZGr3Ylrda6Oz4icuIM45QgYzYYwaNxybbwHmVWUejYsP7qtHcPlXYzU_S3Kp8UNU5B5oHHaoDNfqfulMJOgL8MUV0YEr7CTs8sjw2mqsYHvcqLtZhyEMf00mB_7xdXfSjaVLQUs_GQJHyCha30b7_bsrsiX3SQy6zapLJVas1BoROtIqlwSjC7WFXpY2P0-R-1WLaEcDUaoM07q45AfjcomUaOiN3dm32i7RXugvYNISjgMCI8SyD_4UR1O-zVI46OfLFTNClXIUMMiebmkVTk6dYYrPCA4aZUCuiLiggZ3QEUOZM2uGM6lDwibuj0pCa3WZq9dGKcuEo2GyUhhyN7EWSbvRuKswglpvzZS5n41ux3FLySQSrzRO1Krlykb8OahoMHCCepYu8FGl8PozLw_69fN8Xt6plUbYJdPet7FP1CNh1nR8VGyw1gEYd1Q44BljdANdzQKC4t04mXVC4jTCGtP9auX1PGwm2KSqgpJElGwb49FWsInNPcC7AmCDK_gp88Q3biZ0W4Xp3uxVpk9EDaCjn3A8bB5vh9YBahu5viEdh0lZwgxdVIAaGyMhTspd2Ycvcap-hDfB9FETMosLT4Rgm-DgVl2_jz5fx2eNHRhowNTp44wPOaAvuT4Ch8vkVhkUpb6iMsCm9NpCusZ__tAsGuyAHNtF564QeI6hxfwFXRdiPiDZdPFiOFK1AnC05n-RroGNOfJ9KYnJaq-BepteY-rf-rDBRYOfVZAqHuxHvHVD4JCElD-31nZAt0nhOZ7lGC0RC2PzAXtMVzRJMBjolOS59PKvA5Ln_RMcg2PZr8r2Iq5-WjAPcj66iQUQy5_QWuE1igzadCx4XPrde_s2iTcgBGebkOP27Fp-AIFcuE-exCBVDwaphfx5pq2KEXdpA8C3uEN97n8SlQShX5a0EyX5LxhTzX2Mbc2wvOlOKdFmBvICkOfTeE1RQ58i160SvsnnUv8oh0HlkH2y5cLhCUNZgh-VGsa2LOVXWlBOqCpHOcbTOu6-1XyYXAdl8bPIlJ_J8X99fL8sgZaGfolJllRTRo8Scs-La1zWzeNXsGXGGTssLKbmodBWFztHjpN5wTecXAPHYlJ193Ib7G_x2OY2LuVise3UryDTi7_9te6kB38RQiG9ZWE5--DOWK_BcA12EHFdOn2EF3GMHUxob_u80qz9kcN858hr4hK5RtwnNalKnBvgd-KtlDPSVSILZs5mHiypAX5iIv0EM9-50S3ILxjN6DtWZOst--IZL6lJZqZOTFbem2fJUdu-BtPwROyaHEUZhgyc2a9EBUTuTZ7qUeo41InhsKFgsav6Le54PR4OamcLckUcFcvoOX7dF4rXJRwn78lkCg9BFfCMIefB9WYmRWGHl9jN2MGcr7Tej-cpoRqncolwyDI_mZh7cd1DaYcWpXyxEj3UTBn8y4p6_1xamrc0LCuT6D15GtVnryuJpP80Z7C1BXo5zDrH4yXYS1NJbY0xMHc3rdU3CxZRsjjSnIm5DjlsBGXyf4Hh0OH41wtT_LkTYohPLsMd6ozlwAMZIlBaJZGnjzYlRyiIS-sCg4DZ5fcXMLlsHkXTaEIphbJqS8xkPUWNAZLXZvMJVORywkpjhfbP5EWWZGkgoExPYMqeGPz0l-RnHv408NvkmVBsBizeaifihThpJTNxUQDU73yJ71NX0-Gt3K2KDPf3ywBAyDyA5w2QckskEgxMqrrwsHbDW1Z7hYxAZQfW259j8nOZbfszL4Z4NQB88tgA8-5AVw5sWHvQAEbmFcMGAmxngXfjivBwj435P0W37JYbnt5Ez6urT9hZKUhKTirbmLY8gTZ4GEK3ebp7wDuXSVT_misz-33OeoM0w1a5CM-6QyYvBbEhPMpbIflzP7jodoevcddbG_p1r47gyuoE3UHEwvfTIMWlyK6x1LOw_P1yKnCdCFGjxbfpR_EQHuJFQ7Bi1lPeAJv8QUIdcmR3FuayhmZX5v6mSmoGkMHo-jjHYdNRX2wkHL3mnARFjp1uiCHxyVHlFhs71Rq41M76xxrh82gLj2pchVTpzOOBOoP9AgDfGHdHPOU7LdiJTkIeXwdpAakglAEH_u2PadIQRKBYhVOdY5174Q9PMmFoiOnqcLQPgd6ESZOxd_W-yoXIG05vnUWRgCOq_12A9QkNvlnyRykvThm26DQrpzC9CljALn81jXd4vOjMvuSxOusSU-8OBWFi87gX4Uz20Jld_5HKA2q-M6iY1xBTMIboArz7OV-VaMJMRvfktVfLsPJL6wWmOple-B2W5jyMmvCa2jr6FkQEbgVWKSCUb6Z4UGFk-h4ogmDtLDrb8044Yo0lYMeMF7hPEz_DB5QfM9RhwBTI0pssyfQ6XeXeHVXHl8rqiiIigtKLmROvyaBdBW1C8MXiXze3RfEFALBn3viHgjjPL_M6oitCTWaEVZ_VydjUsKcrJGUF4DxSx3dIXdDIAjSDTVJI6pNdSYCTqe_AZMVgseX6-FX-zT2L7dqoTGGQFng8iq_3XaHLhBm6-nSQaig8xUvPLAIcjLIDvs1-PanSM7oJtUeUbulMJzGNGiLnZ27onvPqVWrE482u4VA52e50MaYgDfD7QsEMo1IGnVnGWwR5U3BW05u4KKS-VWMx63BsnQ2UUWb6VakiQuZX3WQ2bQMo9Uh0dSfOC4su676vTVHzOL-K2M9RT9gLWc6qTyZaoSv9eNB_hxkzxOUQNfSsi6M-09U4v_2bl_J00PGlOHnw2O2Vy6Dexe1VPhxBJVn4fkNbtM0dRm8BrJ9zBF3kFO-1P_WQ4pJcUM-0njuKjt-J69IU76LPplShlSbh9-tKCsOiuY8WCjtzCBRrMY-dm2yYPJxHY7onYnz7_ARWhwxfe7dPA_YnMnSIhnFbax3RVtQCv_IsGmoKxt8D6JRJLCopqXBpBTBUlLxlUAIInFX3xskX_UifdUvSiVitxWKqwSfkSDCrE2t1YEu2VDdd-CsGNYuGgbEGRuanW1FEHbc_KGnluNLHBMx0LJYu9iXv5Yu3GuRODpJk_QA5uXFQOuGlokwT_2EJt8pWhbUsr6C8wW42QV22cHA0OAZ5l86npofSA5RkI12aNVsVY7cRDFvK9-20pSRkPbII2tOIwAnCn6CvnhOW1T1xWRD3E10cZ1aE6_nLtnOIJJcedj0oVO87IMngoKOfCS1do5MbtL8fIyQkoOSAJlXB7F7-8odd-HkpHUrTMKOiL4O51G7SpBtrew3BfimKsLPO2q72lS9AqXggruMziMKOPMwLN_OFzEYFqguL02u55f5x-ckeWRdSW0tp70WYdfSr-Kzp7a_V5V4NuX42j4tismRDjXh6tM0c7RJ1heDxtPLo2bF6x7SwwbHB6bQbqZeCM6LNODce1STKqFGPSvge5KQZARoC2atEhV2Zce4zQVf0EZ8PbZ93JGmLvlyeltNkuLPX-Pzz6qZaxbiP2rky_iBe16ns0MUQj1mCbUkdDX6nvz2BYHx38wgJCHEz6JOfgzPw7a64S14u07HQcLVqEJIl0VmNd4I6ufixdeGjYfdlWdRhf9JTiZTOD1mFRC8OuuJrVuUGroh3NnRwiwoDMt2oJpYvDjsIJdyw5yzofPWEmXb0fQ3wjakr0Wc2Si216OeFQBIgl9Qj8VylnZLHwlOiAQEEvHhwpgnwPqa8xpMxHZY5-Vh-5T6B1ctHbYfjOU9gMuq5_b8TKDxm6E1i4ecWvuPlLvFwJfa4w95Ad-aUxWOVWPXi63V8qatk7qfnlmLdjzt5FLAH-NSLx3EJiIIw6MNJ2ld-HMwvsHtx6r3AAd7y-rn7NyIXusFZdF10K; .AspNetCore.Identity.ApplicationC2=NgLzc1iW8KgBqdqCva-KxGKiWOX4LECYVCsUCkt8R_056yupHzEjgQwpyfstgRACLePPy1sdWfsp5Zik6uNUZnfaMDpMZEUua0CsOd02ZkgVu34VT5N95ImP_bu-jwWewjuOVGeEXT2rxdHccNLEdFNv2dK1pwIpDYHhAVtHUQSVzaERAKHSm-5-l8sSKyHM93_l0MAIxWL4MUea7KI3SONM1Sv_GZy4xBUVg9abd-E8VQEcDm5rLvT35vSkRc8WBwtyeZN_uSqXB2QR1so6B_EiwfiKPFN6xiB9s_TcC_Bi1Qa89c3XQgnICvzYLVH1n7N2D6RK1U5gakd63vmA4Lgz3EUOmMih1Jeloe24-P_yQbOVIORT53BHtrKoaVNogWuP5vj7hJx1Yjfd-a_wIC-su3y16Zamcu-T9Aj-k1Bg0a68Uo48DF2UN3UWKE_-bcnBYrVt7J1-Sov7LpN9TyR_22BS6JTmS2uh5FGo5UPv5lUmrxT7kvuuQ4kHMwGiCXhHtvdiRKpfp8dReygdBSeXlwx5QYAnAOXa-OniCH4OIaiBN6q5z8Ir51qm_x5wcy4tMfeRy3lD1OIcEYbZmxflkFT_NGyMkV3k2BLRb9gTtopa9PV73SFVAZebnXq6lobOOZ7njnM5HzMkgKeUcsbFAUIq20Gw2QtfZd_4OeR47Gvumb8u9gviL9BQMvtxs7bxLK_8QE8nhUJxA3YWLZnsJ0fWa-kBmqJT-EJyTupc-xRuBfgnfwiMkIWraeN5S8KzZEbIrT1pfW04DFs2dgGcvN_ferXbLjZBVg2Qa50EoPGXJ3dzyBhZYjwR_pLZBQs6hNDOVeBZAFyB3aC3nmPlI6n_3aZPNm4kU07ktvnXwLGD25epl3UwP2-sSrnxYxEmh5qtIf2QmhgKsYLiearCKdGDZuIngerSGkTNyDD1YsFrOwbLipLuSyBtOzVuyQTWcOqg1UKkLkgiiu8DxyC2GAanarh_TvbtAmSDju0DOjXvgZIJRt9pM9b3S9fA2sCyhi4sAc5tLTqQBcxZKzgxLNBlg1A78vTo0xJO02TL0FOpVoQesJLqquWUThRW0Vs_ztHwMzbjyJTbukrTvJqqsMHZ1HZcsVHN4La_0MekBGDI_ARzC9lts27bON_w6vi_0GWp-iV-VStl4eLNEaGRYN070GdxETuRwHPJQ5yoYgeJd9jyLVimb7CqyfU6U2UJUEy1Hls8CAwtKi22Vu7T4CVLoAVxj5WTWX8s3eSCM1GTEpYhx9jwc8xtOkRl6w4gGFSMFHEx8RYkXPpnzwgs0uK3a7jaXtCaYs69OFKQNFuDmR4ABNH_vY6-a732HuwkaGkQKNRjNejja0cJl5NZaNQ6Ku746n3h0epKD_ITuW856pZxYKIVgkMcsqPxQu_4GeBC1LbCmGmiWyA6A7YgVUoNVhdQsbs5bNI9PyIW782k2lM7WeWy5UbPXGkRsp1YQ2xKWo-EJn7l41ItiuSxpZm-Seq8WGjaSXcoLK93evMh8MxBuaoM7C4QjHfXnNhTRWdo_x4it2acfkmbUlh4ZLRnO3xt6zzQpnQ-mefGF_W5nTYSt6qU1SJpzJIyhEL4n0OePe5WsNy_s-k_aWLhC1Bl4D3LBvCpQY2TfmP01AkIIoUgF0CqhaqnOm-QbC72kPFOuSdKenNXRB2cjPVtW9BkxQK4ygi7gHsNd2ABHIqVkFmhkSgJy4UOYHQ0kekyIHBYUwmz6FuoPEmP3PW3KUeKsLSTbwFJF2XIIb1ItGfpD__H0qY7tKQf-AfO6k4Iyr16skAaUL-iEc9e7fK-b4JOzTgJvr4sVQJdXKO7Y1BhIGcaZ690OFlFxnaCXtKUX_-cu3WwBFmcr8X1V4tEgZuzjxxAQ0Go5tzrvMuWl0sJxZmck0aq6y5J8kK0CHXe2lHMRGdb4hafrZkLz0dtWjAGA6BK6-bBM6KOWRza8PIRMDeubIybLwI8BDoDUrydzoEA-yPPA63Ehj-Uhy7MqQ2PAzps-ww4nk6dt7_zj-K4vb1QOZSNOkluihRs-geh1v6zO6hZEJctZ2BgYVOzoUYMNibbALQnK5jaHJe9CjI61xVVuXjp2UA8iznhqs7nGn3MW0kKyMGyUSrADiaytKFb3GGXbZ0PCSj3qOY6-L_TQwJtwlNwBGvd02kAEhtcKUxdQfC4T6ZJz0BNEcc0I-4bWYeKClxGxQHhq8GXuEvFlCFx9wsZB57kPdJZQ4XC2IRfjj0vYSyhQmRlsMgi5zRZhJN-vaUcBbN228pumSE-8DRx29PV9EsbxOJl0kOdh7HxmNMAmYemiINEvKIF-C_zYxRX-emytwIwHEV6hdqGhbv1B2iHFtOAdupRYdwLdFzC4yZmyXQcFMlOqhhx_UR_ItOMsCytld3pAPT_kepRZJyzQFs7LcmP9IJxutCaWxKh0uPQUkAVaUh9CWnlF--YQxC_p_Z4KWayRNlesjqA0S_UnOa5OHngGagWrK97U4EI3ozuscd7wglpGrFNJUczlYWA_l6DCTb5BYupnSjHDh_IGVY7Bu9kiIYbafJWt3-lG5p5KK2mboPVLWE8zxh4TG0lu-le9vNGtTcCKAMLLH2inaexEwWbNliwjTSUn2E7CgfW9Iu_VP8wLFDMOp5juoq3s4JebT6EGWBaoobqkZEmsee4CXVc1FdWReBiexZKSZ-o8wIWeXCBFZCmm2NdDRAKaPmQJ4CA5QlIty-mcVRQ3FE5Eez93XaQQeZNiVZhv6lHRdRUQgC-rFy_brHqIN2xkSsMwCHZ2VnJudV45IhOcttDsSxUR4sWr3_nUK_w9dJ5QsCdFM8BJKcK1gPHSWPJieIdDjgkOx9P05rM-7nqNqOA; XSRF-TOKEN=CfDJ8OFOJERUl99Io1MZi0D5PWrBM5ofPEr_0w-OUgwrsh1sUwRX4Oo2sqR2UMcvZypw0ppGAL-qiMl_rXZqtIorzdPc2nAJLxBCs92HgSPw0wK3EQFQoaLfiZoec5st2cNOuvsazHcWo19shMbTEgKthgbWCJz5pHyLZIdlwNCYmdFV-AITJ4xAjgsAk42lJ5aapw\r\n\r\n";
            var requestBytes = Encoding.UTF8.GetBytes(bigRequest);
            await _input.Writer.WriteAsync(requestBytes);

            // Make a 3rd empty block
            _input.Writer.Alloc(2048).Commit();

            var result = await _input.Reader.ReadAsync();
            var examined = result.Buffer.End;
            var consumed = result.Buffer.End;

            _frame.ParseRequest(result.Buffer, out consumed, out examined);
            _input.Reader.Advance(consumed, examined);

            _frame.Reset();

            await _input.Writer.WriteAsync(requestBytes);

            result = await _input.Reader.ReadAsync();
            examined = result.Buffer.End;
            consumed = result.Buffer.End;

            _frame.ParseRequest(result.Buffer, out consumed, out examined);
            _input.Reader.Advance(consumed, examined);
        }

        [Fact]
        public async Task TakeStartLineThrowsWhenTooLong()
        {
            _serviceContext.ServerOptions.Limits.MaxRequestLineSize = "GET / HTTP/1.1\r\n".Length;

            var requestLineBytes = Encoding.ASCII.GetBytes("GET /a HTTP/1.1\r\n");
            await _input.Writer.WriteAsync(requestLineBytes);

            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;
            var exception = Assert.Throws<BadHttpRequestException>(() => _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal(CoreStrings.BadRequest_RequestLineTooLong, exception.Message);
            Assert.Equal(StatusCodes.Status414UriTooLong, exception.StatusCode);
        }

        [Theory]
        [MemberData(nameof(TargetWithEncodedNullCharData))]
        public async Task TakeStartLineThrowsOnEncodedNullCharInTarget(string target)
        {
            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal(CoreStrings.FormatBadRequest_InvalidRequestTarget_Detail(target), exception.Message);
        }

        [Theory]
        [MemberData(nameof(TargetWithNullCharData))]
        public async Task TakeStartLineThrowsOnNullCharInTarget(string target)
        {
            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal(CoreStrings.FormatBadRequest_InvalidRequestTarget_Detail(target.EscapeNonPrintable()), exception.Message);
        }

        [Theory]
        [MemberData(nameof(MethodWithNullCharData))]
        public async Task TakeStartLineThrowsOnNullCharInMethod(string method)
        {
            var requestLine = $"{method} / HTTP/1.1\r\n";

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes(requestLine));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal(CoreStrings.FormatBadRequest_InvalidRequestLine_Detail(requestLine.EscapeNonPrintable()), exception.Message);
        }

        [Theory]
        [MemberData(nameof(QueryStringWithNullCharData))]
        public async Task TakeStartLineThrowsOnNullCharInQueryString(string queryString)
        {
            var target = $"/{queryString}";

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal(CoreStrings.FormatBadRequest_InvalidRequestTarget_Detail(target.EscapeNonPrintable()), exception.Message);
        }

        [Theory]
        [MemberData(nameof(TargetInvalidData))]
        public async Task TakeStartLineThrowsWhenRequestTargetIsInvalid(string method, string target)
        {
            var requestLine = $"{method} {target} HTTP/1.1\r\n";

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes(requestLine));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal(CoreStrings.FormatBadRequest_InvalidRequestTarget_Detail(target.EscapeNonPrintable()), exception.Message);
        }

        [Theory]
        [MemberData(nameof(MethodNotAllowedTargetData))]
        public async Task TakeStartLineThrowsWhenMethodNotAllowed(string requestLine, HttpMethod allowedMethod)
        {
            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes(requestLine));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal(405, exception.StatusCode);
            Assert.Equal(CoreStrings.BadRequest_MethodNotAllowed, exception.Message);
            Assert.Equal(HttpUtilities.MethodToString(allowedMethod), exception.AllowedHeader);
        }

        [Fact]
        public void ProcessRequestsAsyncEnablesKeepAliveTimeout()
        {
            var requestProcessingTask = _frame.ProcessRequestsAsync();

            var expectedKeepAliveTimeout = _serviceContext.ServerOptions.Limits.KeepAliveTimeout.Ticks;
            _timeoutControl.Verify(cc => cc.SetTimeout(expectedKeepAliveTimeout, TimeoutAction.CloseConnection));

            _frame.Stop();
            _input.Writer.Complete();

            requestProcessingTask.Wait();
        }

        [Fact]
        public async Task WriteThrowsForNonBodyResponse()
        {
            // Arrange
            ((IHttpResponseFeature)_frame).StatusCode = StatusCodes.Status304NotModified;

            // Act/Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _frame.WriteAsync(new ArraySegment<byte>(new byte[1])));
        }

        [Fact]
        public async Task WriteAsyncThrowsForNonBodyResponse()
        {
            // Arrange
            _frame.HttpVersion = "HTTP/1.1";
            ((IHttpResponseFeature)_frame).StatusCode = StatusCodes.Status304NotModified;

            // Act/Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _frame.WriteAsync(new ArraySegment<byte>(new byte[1]), default(CancellationToken)));
        }

        [Fact]
        public async Task WriteDoesNotThrowForHeadResponse()
        {
            // Arrange
            _frame.HttpVersion = "HTTP/1.1";
            ((IHttpRequestFeature)_frame).Method = "HEAD";

            // Act/Assert
            await _frame.WriteAsync(new ArraySegment<byte>(new byte[1]));
        }

        [Fact]
        public async Task WriteAsyncDoesNotThrowForHeadResponse()
        {
            // Arrange
            _frame.HttpVersion = "HTTP/1.1";
            ((IHttpRequestFeature)_frame).Method = "HEAD";

            // Act/Assert
            await _frame.WriteAsync(new ArraySegment<byte>(new byte[1]), default(CancellationToken));
        }

        [Fact]
        public async Task ManuallySettingTransferEncodingThrowsForHeadResponse()
        {
            // Arrange
            _frame.HttpVersion = "HTTP/1.1";
            ((IHttpRequestFeature)_frame).Method = "HEAD";

            // Act
            _frame.ResponseHeaders.Add("Transfer-Encoding", "chunked");

            // Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _frame.FlushAsync());
        }

        [Fact]
        public async Task ManuallySettingTransferEncodingThrowsForNoBodyResponse()
        {
            // Arrange
            _frame.HttpVersion = "HTTP/1.1";
            ((IHttpResponseFeature)_frame).StatusCode = StatusCodes.Status304NotModified;

            // Act
            _frame.ResponseHeaders.Add("Transfer-Encoding", "chunked");

            // Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _frame.FlushAsync());
        }

        [Fact]
        public async Task RequestProcessingTaskIsUnwrapped()
        {
            var requestProcessingTask = _frame.ProcessRequestsAsync();

            var data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost:\r\n\r\n");
            await _input.Writer.WriteAsync(data);

            _frame.Stop();
            Assert.IsNotType(typeof(Task<Task>), requestProcessingTask);

            await requestProcessingTask.TimeoutAfter(TimeSpan.FromSeconds(10));
            _input.Writer.Complete();
        }

        [Fact]
        public async Task RequestAbortedTokenIsResetBeforeLastWriteWithContentLength()
        {
            _frame.ResponseHeaders["Content-Length"] = "12";

            // Need to compare WaitHandle ref since CancellationToken is struct
            var original = _frame.RequestAborted.WaitHandle;

            foreach (var ch in "hello, worl")
            {
                await _frame.WriteAsync(new ArraySegment<byte>(new[] { (byte)ch }));
                Assert.Same(original, _frame.RequestAborted.WaitHandle);
            }

            await _frame.WriteAsync(new ArraySegment<byte>(new[] { (byte)'d' }));
            Assert.NotSame(original, _frame.RequestAborted.WaitHandle);
        }

        [Fact]
        public async Task RequestAbortedTokenIsResetBeforeLastWriteAsyncWithContentLength()
        {
            _frame.ResponseHeaders["Content-Length"] = "12";

            // Need to compare WaitHandle ref since CancellationToken is struct
            var original = _frame.RequestAborted.WaitHandle;

            foreach (var ch in "hello, worl")
            {
                await _frame.WriteAsync(new ArraySegment<byte>(new[] { (byte)ch }), default(CancellationToken));
                Assert.Same(original, _frame.RequestAborted.WaitHandle);
            }

            await _frame.WriteAsync(new ArraySegment<byte>(new[] { (byte)'d' }), default(CancellationToken));
            Assert.NotSame(original, _frame.RequestAborted.WaitHandle);
        }

        [Fact]
        public async Task RequestAbortedTokenIsResetBeforeLastWriteAsyncAwaitedWithContentLength()
        {
            _frame.ResponseHeaders["Content-Length"] = "12";

            // Need to compare WaitHandle ref since CancellationToken is struct
            var original = _frame.RequestAborted.WaitHandle;

            foreach (var ch in "hello, worl")
            {
                await _frame.WriteAsyncAwaited(new ArraySegment<byte>(new[] { (byte)ch }), default(CancellationToken));
                Assert.Same(original, _frame.RequestAborted.WaitHandle);
            }

            await _frame.WriteAsyncAwaited(new ArraySegment<byte>(new[] { (byte)'d' }), default(CancellationToken));
            Assert.NotSame(original, _frame.RequestAborted.WaitHandle);
        }

        [Fact]
        public async Task RequestAbortedTokenIsResetBeforeLastWriteWithChunkedEncoding()
        {
            // Need to compare WaitHandle ref since CancellationToken is struct
            var original = _frame.RequestAborted.WaitHandle;

            _frame.HttpVersion = "HTTP/1.1";
            await _frame.WriteAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes("hello, world")), default(CancellationToken));
            Assert.Same(original, _frame.RequestAborted.WaitHandle);

            await _frame.ProduceEndAsync();
            Assert.NotSame(original, _frame.RequestAborted.WaitHandle);
        }

        [Fact]
        public async Task ExceptionDetailNotIncludedWhenLogLevelInformationNotEnabled()
        {
            var previousLog = _serviceContext.Log;

            try
            {
                var mockTrace = new Mock<IKestrelTrace>();
                mockTrace
                    .Setup(trace => trace.IsEnabled(LogLevel.Information))
                    .Returns(false);

                _serviceContext.Log = mockTrace.Object;

                await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"GET /%00 HTTP/1.1\r\n"));
                var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

                var exception = Assert.Throws<BadHttpRequestException>(() =>
                    _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
                _input.Reader.Advance(_consumed, _examined);

                Assert.Equal(CoreStrings.FormatBadRequest_InvalidRequestTarget_Detail(string.Empty), exception.Message);
                Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
            }
            finally
            {
                _serviceContext.Log = previousLog;
            }
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(5, 5)]
        [InlineData(100, 100)]
        [InlineData(600, 100)]
        [InlineData(700, 1)]
        [InlineData(1, 700)]
        public async Task AcceptsHeadersAcrossSends(int header0Count, int header1Count)
        {
            _serviceContext.ServerOptions.Limits.MaxRequestHeaderCount = header0Count + header1Count;

            var headers0 = MakeHeaders(header0Count);
            var headers1 = MakeHeaders(header1Count, header0Count);

            var requestProcessingTask = _frame.ProcessRequestsAsync();

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\n"));
            await WaitForCondition(TimeSpan.FromSeconds(1), () => _frame.RequestHeaders != null);
            Assert.Equal(0, _frame.RequestHeaders.Count);

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes(headers0));
            await WaitForCondition(TimeSpan.FromSeconds(1), () => _frame.RequestHeaders.Count >= header0Count);
            Assert.Equal(header0Count, _frame.RequestHeaders.Count);

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes(headers1));
            await WaitForCondition(TimeSpan.FromSeconds(1), () => _frame.RequestHeaders.Count >= header0Count + header1Count);
            Assert.Equal(header0Count + header1Count, _frame.RequestHeaders.Count);

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
            Assert.Equal(header0Count + header1Count, _frame.RequestHeaders.Count);

            await requestProcessingTask.TimeoutAfter(TimeSpan.FromSeconds(10));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(5, 5)]
        [InlineData(100, 100)]
        [InlineData(600, 100)]
        [InlineData(700, 1)]
        [InlineData(1, 700)]
        public async Task KeepsSameHeaderCollectionAcrossSends(int header0Count, int header1Count)
        {
            _serviceContext.ServerOptions.Limits.MaxRequestHeaderCount = header0Count + header1Count;

            var headers0 = MakeHeaders(header0Count);
            var headers1 = MakeHeaders(header1Count, header0Count);

            var requestProcessingTask = _frame.ProcessRequestsAsync();

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\n"));
            await WaitForCondition(TimeSpan.FromSeconds(1), () => _frame.RequestHeaders != null);
            Assert.Equal(0, _frame.RequestHeaders.Count);

            var newRequestHeaders = new RequestHeadersWrapper(_frame.RequestHeaders);
            _frame.RequestHeaders = newRequestHeaders;
            Assert.Same(newRequestHeaders, _frame.RequestHeaders);

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes(headers0));
            await WaitForCondition(TimeSpan.FromSeconds(1), () => _frame.RequestHeaders.Count >= header0Count);
            Assert.Same(newRequestHeaders, _frame.RequestHeaders);
            Assert.Equal(header0Count, _frame.RequestHeaders.Count);

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes(headers1));
            await WaitForCondition(TimeSpan.FromSeconds(1), () => _frame.RequestHeaders.Count >= header0Count + header1Count);
            Assert.Same(newRequestHeaders, _frame.RequestHeaders);
            Assert.Equal(header0Count + header1Count, _frame.RequestHeaders.Count);

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
            Assert.Same(newRequestHeaders, _frame.RequestHeaders);
            Assert.Equal(header0Count + header1Count, _frame.RequestHeaders.Count);

            await requestProcessingTask.TimeoutAfter(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void ThrowsWhenMaxRequestBodySizeIsSetAfterReadingFromRequestBody()
        {
            // Act
            // This would normally be set by the MessageBody during the first read.
            _frame.HasStartedConsumingRequestBody = true;

            // Assert
            Assert.True(((IHttpMaxRequestBodySizeFeature)_frame).IsReadOnly);
            var ex = Assert.Throws<InvalidOperationException>(() => ((IHttpMaxRequestBodySizeFeature)_frame).MaxRequestBodySize = 1);
            Assert.Equal(CoreStrings.MaxRequestBodySizeCannotBeModifiedAfterRead, ex.Message);
        }

        [Fact]
        public void ThrowsWhenMaxRequestBodySizeIsSetToANegativeValue()
        {
            // Assert
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ((IHttpMaxRequestBodySizeFeature)_frame).MaxRequestBodySize = -1);
            Assert.StartsWith(CoreStrings.NonNegativeNumberOrNullRequired, ex.Message);
        }

        private static async Task WaitForCondition(TimeSpan timeout, Func<bool> condition)
        {
            const int MaxWaitLoop = 150;

            var delay = (int)Math.Ceiling(timeout.TotalMilliseconds / MaxWaitLoop);

            var waitLoop = 0;
            while (waitLoop < MaxWaitLoop && !condition())
            {
                // Wait for parsing condition to trigger
                await Task.Delay(delay);
                waitLoop++;
            }
        }

        private static string MakeHeaders(int count, int startAt = 0)
        {
            return string.Join("", Enumerable
                .Range(0, count)
                .Select(i => $"Header-{startAt + i}: value{startAt + i}\r\n"));
        }

        public static IEnumerable<object> RequestLineValidData => HttpParsingData.RequestLineValidData;

        public static IEnumerable<object> RequestLineDotSegmentData => HttpParsingData.RequestLineDotSegmentData;

        public static TheoryData<string> TargetWithEncodedNullCharData
        {
            get
            {
                var data = new TheoryData<string>();

                foreach (var target in HttpParsingData.TargetWithEncodedNullCharData)
                {
                    data.Add(target);
                }

                return data;
            }
        }

        public static TheoryData<string, string> TargetInvalidData
            => HttpParsingData.TargetInvalidData;

        public static TheoryData<string, HttpMethod> MethodNotAllowedTargetData
            => HttpParsingData.MethodNotAllowedRequestLine;

        public static TheoryData<string> TargetWithNullCharData
        {
            get
            {
                var data = new TheoryData<string>();

                foreach (var target in HttpParsingData.TargetWithNullCharData)
                {
                    data.Add(target);
                }

                return data;
            }
        }

        public static TheoryData<string> MethodWithNullCharData
        {
            get
            {
                var data = new TheoryData<string>();

                foreach (var target in HttpParsingData.MethodWithNullCharData)
                {
                    data.Add(target);
                }

                return data;
            }
        }

        public static TheoryData<string> QueryStringWithNullCharData
        {
            get
            {
                var data = new TheoryData<string>();

                foreach (var target in HttpParsingData.QueryStringWithNullCharData)
                {
                    data.Add(target);
                }

                return data;
            }
        }

        public static TheoryData<TimeSpan> RequestBodyTimeoutDataValid => new TheoryData<TimeSpan>
        {
            TimeSpan.FromTicks(1),
            TimeSpan.MaxValue,
            Timeout.InfiniteTimeSpan,
            TimeSpan.FromMilliseconds(-1) // Same as Timeout.InfiniteTimeSpan
        };

        public static TheoryData<TimeSpan> RequestBodyTimeoutDataInvalid => new TheoryData<TimeSpan>
        {
            TimeSpan.MinValue,
            TimeSpan.FromTicks(-1),
            TimeSpan.Zero
        };

        public static TheoryData<MinDataRate> MinDataRateData => new TheoryData<MinDataRate>
        {
            null,
            new MinDataRate(bytesPerSecond: 1, gracePeriod: TimeSpan.MaxValue)
        };

        private class RequestHeadersWrapper : IHeaderDictionary
        {
            IHeaderDictionary _innerHeaders;

            public RequestHeadersWrapper(IHeaderDictionary headers)
            {
                _innerHeaders = headers;
            }

            public StringValues this[string key] { get => _innerHeaders[key]; set => _innerHeaders[key] = value; }
            public long? ContentLength { get => _innerHeaders.ContentLength; set => _innerHeaders.ContentLength = value; }
            public ICollection<string> Keys => _innerHeaders.Keys;
            public ICollection<StringValues> Values => _innerHeaders.Values;
            public int Count => _innerHeaders.Count;
            public bool IsReadOnly => _innerHeaders.IsReadOnly;
            public void Add(string key, StringValues value) => _innerHeaders.Add(key, value);
            public void Add(KeyValuePair<string, StringValues> item) => _innerHeaders.Add(item);
            public void Clear() => _innerHeaders.Clear();
            public bool Contains(KeyValuePair<string, StringValues> item) => _innerHeaders.Contains(item);
            public bool ContainsKey(string key) => _innerHeaders.ContainsKey(key);
            public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex) => _innerHeaders.CopyTo(array, arrayIndex);
            public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => _innerHeaders.GetEnumerator();
            public bool Remove(string key) => _innerHeaders.Remove(key);
            public bool Remove(KeyValuePair<string, StringValues> item) => _innerHeaders.Remove(item);
            public bool TryGetValue(string key, out StringValues value) => _innerHeaders.TryGetValue(key, out value);
            IEnumerator IEnumerable.GetEnumerator() => _innerHeaders.GetEnumerator();
        }
    }
}
