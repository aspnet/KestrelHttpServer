
using System;
using System.Collections.Generic;

namespace Microsoft.AspNet.Server.Kestrel.Http 
{
    public partial class Frame
    {
        
        private object _currentIHttpRequestFeature;
        private object _currentIHttpResponseFeature;
        private object _currentIHttpRequestIdentifierFeature;
        private object _currentIHttpSendFileFeature;
        private object _currentIServiceProvidersFeature;
        private object _currentIHttpAuthenticationFeature;
        private object _currentIHttpRequestLifetimeFeature;
        private object _currentIQueryFeature;
        private object _currentIFormFeature;
        private object _currentIResponseCookiesFeature;
        private object _currentIItemsFeature;
        private object _currentIHttpConnectionFeature;
        private object _currentITlsConnectionFeature;
        private object _currentIHttpUpgradeFeature;
        private object _currentIHttpWebSocketFeature;
        private object _currentISessionFeature;

        private void FastReset()
        {
            _currentIHttpRequestFeature = this;
            _currentIHttpResponseFeature = this;
            _currentIHttpRequestIdentifierFeature = null;
            _currentIHttpSendFileFeature = null;
            _currentIServiceProvidersFeature = null;
            _currentIHttpAuthenticationFeature = null;
            _currentIHttpRequestLifetimeFeature = null;
            _currentIQueryFeature = null;
            _currentIFormFeature = null;
            _currentIResponseCookiesFeature = null;
            _currentIItemsFeature = null;
            _currentIHttpConnectionFeature = null;
            _currentITlsConnectionFeature = null;
            _currentIHttpUpgradeFeature = this;
            _currentIHttpWebSocketFeature = null;
            _currentISessionFeature = null;
        }

        private object FastFeatureGet(Type key)
        {
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestFeature))
            {
                return _currentIHttpRequestFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpResponseFeature))
            {
                return _currentIHttpResponseFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestIdentifierFeature))
            {
                return _currentIHttpRequestIdentifierFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpSendFileFeature))
            {
                return _currentIHttpSendFileFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IServiceProvidersFeature))
            {
                return _currentIServiceProvidersFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Authentication.IHttpAuthenticationFeature))
            {
                return _currentIHttpAuthenticationFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestLifetimeFeature))
            {
                return _currentIHttpRequestLifetimeFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IQueryFeature))
            {
                return _currentIQueryFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IFormFeature))
            {
                return _currentIFormFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IResponseCookiesFeature))
            {
                return _currentIResponseCookiesFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IItemsFeature))
            {
                return _currentIItemsFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpConnectionFeature))
            {
                return _currentIHttpConnectionFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.ITlsConnectionFeature))
            {
                return _currentITlsConnectionFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpUpgradeFeature))
            {
                return _currentIHttpUpgradeFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpWebSocketFeature))
            {
                return _currentIHttpWebSocketFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.ISessionFeature))
            {
                return _currentISessionFeature;
            }
            object feature = null;
            if (MaybeExtra?.TryGetValue(key, out feature) ?? false) 
            {
                return feature;
            }
            return null;
        }

        private void FastFeatureSet(Type key, object feature)
        {
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestFeature))
            {
                _currentIHttpRequestFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpResponseFeature))
            {
                _currentIHttpResponseFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestIdentifierFeature))
            {
                _currentIHttpRequestIdentifierFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpSendFileFeature))
            {
                _currentIHttpSendFileFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IServiceProvidersFeature))
            {
                _currentIServiceProvidersFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Authentication.IHttpAuthenticationFeature))
            {
                _currentIHttpAuthenticationFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestLifetimeFeature))
            {
                _currentIHttpRequestLifetimeFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IQueryFeature))
            {
                _currentIQueryFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IFormFeature))
            {
                _currentIFormFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IResponseCookiesFeature))
            {
                _currentIResponseCookiesFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IItemsFeature))
            {
                _currentIItemsFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpConnectionFeature))
            {
                _currentIHttpConnectionFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.ITlsConnectionFeature))
            {
                _currentITlsConnectionFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpUpgradeFeature))
            {
                _currentIHttpUpgradeFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpWebSocketFeature))
            {
                _currentIHttpWebSocketFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.ISessionFeature))
            {
                _currentISessionFeature = feature;
                System.Threading.Interlocked.Increment(ref _featureRevision);
                return;
            }
            Extra[key] = feature;
        }

        private IEnumerable<KeyValuePair<Type, object>> FastEnumerable()
        {
            if (_currentIHttpRequestFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestFeature), _currentIHttpRequestFeature);
            }
            if (_currentIHttpResponseFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.IHttpResponseFeature), _currentIHttpResponseFeature);
            }
            if (_currentIHttpRequestIdentifierFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestIdentifierFeature), _currentIHttpRequestIdentifierFeature);
            }
            if (_currentIHttpSendFileFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.IHttpSendFileFeature), _currentIHttpSendFileFeature);
            }
            if (_currentIServiceProvidersFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.Internal.IServiceProvidersFeature), _currentIServiceProvidersFeature);
            }
            if (_currentIHttpAuthenticationFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.Authentication.IHttpAuthenticationFeature), _currentIHttpAuthenticationFeature);
            }
            if (_currentIHttpRequestLifetimeFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestLifetimeFeature), _currentIHttpRequestLifetimeFeature);
            }
            if (_currentIQueryFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.Internal.IQueryFeature), _currentIQueryFeature);
            }
            if (_currentIFormFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.Internal.IFormFeature), _currentIFormFeature);
            }
            if (_currentIResponseCookiesFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.Internal.IResponseCookiesFeature), _currentIResponseCookiesFeature);
            }
            if (_currentIItemsFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.Internal.IItemsFeature), _currentIItemsFeature);
            }
            if (_currentIHttpConnectionFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.IHttpConnectionFeature), _currentIHttpConnectionFeature);
            }
            if (_currentITlsConnectionFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.ITlsConnectionFeature), _currentITlsConnectionFeature);
            }
            if (_currentIHttpUpgradeFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.IHttpUpgradeFeature), _currentIHttpUpgradeFeature);
            }
            if (_currentIHttpWebSocketFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.IHttpWebSocketFeature), _currentIHttpWebSocketFeature);
            }
            if (_currentISessionFeature != null)
            {
                yield return new KeyValuePair<Type, object>(typeof(global::Microsoft.AspNet.Http.Features.ISessionFeature), _currentISessionFeature);
            }
            if (MaybeExtra != null)
            {
                foreach(var item in MaybeExtra)
                {
                    yield return item;
                }
            }
        }
    }
}
