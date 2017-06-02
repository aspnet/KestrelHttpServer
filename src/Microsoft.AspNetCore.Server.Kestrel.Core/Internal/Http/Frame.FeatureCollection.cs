// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public partial class Frame : IFeatureCollection,
                                 IHttpRequestFeature,
                                 IHttpResponseFeature,
                                 IHttpUpgradeFeature,
                                 IHttpConnectionFeature,
                                 IHttpRequestLifetimeFeature,
                                 IHttpRequestIdentifierFeature,
                                 IHttpMaxRequestBodySizeFeature,
                                 IHttpRequestBodyTimeoutFeature
    {
        // NOTE: When feature interfaces are added to or removed from this Frame class implementation,
        // then the list of `implementedFeatures` in the generated code project MUST also be updated.
        // See also: tools/Microsoft.AspNetCore.Server.Kestrel.GeneratedCode/FrameFeatureCollection.cs

        private int _featureRevision;

        private List<KeyValuePair<Type, object>> MaybeExtra;

        public void ResetFeatureCollection()
        {
            FastReset();
            MaybeExtra?.Clear();
            _featureRevision++;
        }

        private object ExtraFeatureGet(Type key)
        {
            if (MaybeExtra == null)
            {
                return null;
            }
            for (var i = 0; i < MaybeExtra.Count; i++)
            {
                var kv = MaybeExtra[i];
                if (kv.Key == key)
                {
                    return kv.Value;
                }
            }
            return null;
        }

        private void ExtraFeatureSet(Type key, object value)
        {
            if (MaybeExtra == null)
            {
                MaybeExtra = new List<KeyValuePair<Type, object>>(2);
            }

            for (var i = 0; i < MaybeExtra.Count; i++)
            {
                if (MaybeExtra[i].Key == key)
                {
                    MaybeExtra[i] = new KeyValuePair<Type, object>(key, value);
                    return;
                }
            }
            MaybeExtra.Add(new KeyValuePair<Type, object>(key, value));
        }

        public IFrameControl FrameControl { get; set; }

        string IHttpRequestFeature.Protocol
        {
            get => HttpVersion;
            set => HttpVersion = value;
        }

        string IHttpRequestFeature.Scheme
        {
            get => Scheme ?? "http";
            set => Scheme = value;
        }

        string IHttpRequestFeature.Method
        {
            get => Method;
            set => Method = value;
        }

        string IHttpRequestFeature.PathBase
        {
            get => PathBase ?? "";
            set => PathBase = value;
        }

        string IHttpRequestFeature.Path
        {
            get => Path;
            set => Path = value;
        }

        string IHttpRequestFeature.QueryString
        {
            get => QueryString;
            set => QueryString = value;
        }

        string IHttpRequestFeature.RawTarget
        {
            get => RawTarget;
            set => RawTarget = value;
        }

        IHeaderDictionary IHttpRequestFeature.Headers
        {
            get => RequestHeaders;
            set => RequestHeaders = value;
        }

        Stream IHttpRequestFeature.Body
        {
            get => RequestBody;
            set => RequestBody = value;
        }

        int IHttpResponseFeature.StatusCode
        {
            get => StatusCode;
            set => StatusCode = value;
        }

        string IHttpResponseFeature.ReasonPhrase
        {
            get => ReasonPhrase;
            set => ReasonPhrase = value;
        }

        IHeaderDictionary IHttpResponseFeature.Headers
        {
            get => ResponseHeaders;
            set => ResponseHeaders = value;
        }

        Stream IHttpResponseFeature.Body
        {
            get => ResponseBody;
            set => ResponseBody = value;
        }

        CancellationToken IHttpRequestLifetimeFeature.RequestAborted
        {
            get => RequestAborted;
            set => RequestAborted = value;
        }

        bool IHttpResponseFeature.HasStarted => HasResponseStarted;

        bool IHttpUpgradeFeature.IsUpgradableRequest => _upgradeAvailable;

        bool IFeatureCollection.IsReadOnly => false;

        int IFeatureCollection.Revision => _featureRevision;

        IPAddress IHttpConnectionFeature.RemoteIpAddress
        {
            get => RemoteIpAddress;
            set => RemoteIpAddress = value;
        }

        IPAddress IHttpConnectionFeature.LocalIpAddress
        {
            get => LocalIpAddress;
            set => LocalIpAddress = value;
        }

        int IHttpConnectionFeature.RemotePort
        {
            get => RemotePort;
            set => RemotePort = value;
        }

        int IHttpConnectionFeature.LocalPort
        {
            get => LocalPort;
            set => LocalPort = value;
        }

        string IHttpConnectionFeature.ConnectionId
        {
            get => ConnectionIdFeature;
            set => ConnectionIdFeature = value;
        }

        string IHttpRequestIdentifierFeature.TraceIdentifier
        {
            get => TraceIdentifier;
            set => TraceIdentifier = value;
        }

        bool IHttpMaxRequestBodySizeFeature.IsReadOnly => HasStartedConsumingRequestBody || _wasUpgraded;

        long? IHttpMaxRequestBodySizeFeature.MaxRequestBodySize
        {
            get => MaxRequestBodySize;
            set
            {
                if (HasStartedConsumingRequestBody)
                {
                    throw new InvalidOperationException(CoreStrings.MaxRequestBodySizeCannotBeModifiedAfterRead);
                }
                if (_wasUpgraded)
                {
                    throw new InvalidOperationException(CoreStrings.MaxRequestBodySizeCannotBeModifiedForUpgradedRequests);
                }
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.NonNegativeNumberOrNullRequired);
                }

                MaxRequestBodySize = value;
            }
        }

        TimeSpan IHttpRequestBodyTimeoutFeature.Timeout => RequestBodyTimeout;

        TimeSpan? IHttpRequestBodyTimeoutFeature.ExtendedTimeout => RequestBodyExtendedTimeout;

        double? IHttpRequestBodyTimeoutFeature.MinimumDataRate => RequestBodyMinimumDataRate;

        void IHttpRequestBodyTimeoutFeature.Configure(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), CoreStrings.PositiveTimeSpanRequired);
            }

            RequestBodyTimeout = timeout;
            RequestBodyExtendedTimeout = null;
            RequestBodyMinimumDataRate = null;
        }

        void IHttpRequestBodyTimeoutFeature.Configure(TimeSpan timeout, TimeSpan extendedTimeout, double minimumDataRate)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), CoreStrings.PositiveTimeSpanRequired);
            }

            if (extendedTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(extendedTimeout), CoreStrings.PositiveTimeSpanRequired);
            }

            if (minimumDataRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumDataRate), CoreStrings.PositiveNumberRequired);
            }

            if (extendedTimeout <= timeout)
            {
                throw new InvalidOperationException(CoreStrings.FormatRequestBodyExtendedTimeoutSmallerThanTimeout(extendedTimeout, timeout));
            }

            RequestBodyTimeout = timeout;
            RequestBodyExtendedTimeout = extendedTimeout;
            RequestBodyMinimumDataRate = minimumDataRate;
        }

        object IFeatureCollection.this[Type key]
        {
            get => FastFeatureGet(key);
            set => FastFeatureSet(key, value);
        }

        TFeature IFeatureCollection.Get<TFeature>()
        {
            return (TFeature)FastFeatureGet(typeof(TFeature));
        }

        void IFeatureCollection.Set<TFeature>(TFeature instance)
        {
            FastFeatureSet(typeof(TFeature), instance);
        }

        void IHttpResponseFeature.OnStarting(Func<object, Task> callback, object state)
        {
            OnStarting(callback, state);
        }

        void IHttpResponseFeature.OnCompleted(Func<object, Task> callback, object state)
        {
            OnCompleted(callback, state);
        }

        async Task<Stream> IHttpUpgradeFeature.UpgradeAsync()
        {
            if (!((IHttpUpgradeFeature)this).IsUpgradableRequest)
            {
                throw new InvalidOperationException(CoreStrings.CannotUpgradeNonUpgradableRequest);
            }

            if (_wasUpgraded)
            {
                throw new InvalidOperationException(CoreStrings.UpgradeCannotBeCalledMultipleTimes);
            }

            if (!ServiceContext.ConnectionManager.UpgradedConnectionCount.TryLockOne())
            {
                throw new InvalidOperationException(CoreStrings.UpgradedConnectionLimitReached);
            }

            _wasUpgraded = true;

            ServiceContext.ConnectionManager.NormalConnectionCount.ReleaseOne();

            StatusCode = StatusCodes.Status101SwitchingProtocols;
            ReasonPhrase = "Switching Protocols";
            ResponseHeaders["Connection"] = "Upgrade";
            if (!ResponseHeaders.ContainsKey("Upgrade"))
            {
                StringValues values;
                if (RequestHeaders.TryGetValue("Upgrade", out values))
                {
                    ResponseHeaders["Upgrade"] = values;
                }
            }

            await FlushAsync(default(CancellationToken));

            return _frameStreams.Upgrade();
        }

        IEnumerator<KeyValuePair<Type, object>> IEnumerable<KeyValuePair<Type, object>>.GetEnumerator() => FastEnumerable().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => FastEnumerable().GetEnumerator();

        void IHttpRequestLifetimeFeature.Abort()
        {
            Abort(error: null);
        }
    }
}
