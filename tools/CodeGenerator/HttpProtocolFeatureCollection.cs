// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace CodeGenerator
{
    // This project can output the Class library as a NuGet Package.
    // To enable this option, right-click on the project and select the Properties menu item. In the Build tab select "Produce outputs on build".
    public class HttpProtocolFeatureCollection
    {
        static string Each<T>(IEnumerable<T> values, Func<T, string> formatter)
        {
            return values.Select(formatter).Aggregate((a, b) => a + b);
        }

        public class KnownFeature
        {
            public Type Type;
            public int Index;
        }

        public static string GeneratedFile(string className)
        {
            var alwaysFeatures = new[]
            {
                typeof(IHttpRequestFeature),
                typeof(IHttpResponseFeature),
                typeof(IHttpRequestIdentifierFeature),
                typeof(IServiceProvidersFeature),
                typeof(IHttpRequestLifetimeFeature),
                typeof(IHttpConnectionFeature),
            };

            var commonFeatures = new[]
            {
                typeof(IHttpAuthenticationFeature),
                typeof(IQueryFeature),
                typeof(IFormFeature),
            };

            var sometimesFeatures = new[]
            {
                typeof(IHttpUpgradeFeature),
                typeof(IHttp2StreamIdFeature),
                typeof(IResponseCookiesFeature),
                typeof(IItemsFeature),
                typeof(ITlsConnectionFeature),
                typeof(IHttpWebSocketFeature),
                typeof(ISessionFeature),
                typeof(IHttpMaxRequestBodySizeFeature),
                typeof(IHttpMinRequestBodyDataRateFeature),
                typeof(IHttpMinResponseDataRateFeature),
                typeof(IHttpBodyControlFeature),
            };

            var rareFeatures = new[]
            {
                typeof(IHttpSendFileFeature),
            };

            var allFeatures = alwaysFeatures.Concat(commonFeatures).Concat(sometimesFeatures).Concat(rareFeatures)
                                            .Select((type, index) => new KnownFeature
                                            {
                                                Type = type,
                                                Index = index
                                            });

            // NOTE: This list MUST always match the set of feature interfaces implemented by HttpProtocol.
            // See also: src/Kestrel/Http/HttpProtocol.FeatureCollection.cs
            var implementedFeatures = new[]
            {
                typeof(IHttpRequestFeature),
                typeof(IHttpResponseFeature),
                typeof(IHttpRequestIdentifierFeature),
                typeof(IHttpRequestLifetimeFeature),
                typeof(IHttpConnectionFeature),
                typeof(IHttpMaxRequestBodySizeFeature),
                typeof(IHttpMinRequestBodyDataRateFeature),
                typeof(IHttpMinResponseDataRateFeature),
                typeof(IHttpBodyControlFeature),
            };

            return $@"// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{{
    public partial class {className}
    {{{Each(allFeatures, feature => $@"
        private static readonly Type {feature.Type.Name}Type = typeof({feature.Type.Name});")}
{Each(allFeatures, feature => $@"
        private object _current{feature.Type.Name};")}

        private void FastReset()
        {{{Each(implementedFeatures, feature => $@"
            _current{feature.Name} = this;")}
            {Each(allFeatures.Where(f => !implementedFeatures.Contains(f.Type)), feature => $@"
            _current{feature.Type.Name} = null;")}
        }}

        object IFeatureCollection.this[Type key]
        {{
            get
            {{
                object feature;{Each(allFeatures, feature => $@"
                {(feature.Index != 0 ? "else " : "")}if (key == {feature.Type.Name}Type)
                {{
                    feature = _current{feature.Type.Name};
                }}")}
                else
                {{
                    feature = ExtraFeatureGet(key);
                }}

                return feature ?? ConnectionFeatures[key];
            }}

            set
            {{
                _featureRevision++;
                {Each(allFeatures, feature => $@"
                {(feature.Index != 0 ? "else " : "")}if (key == {feature.Type.Name}Type)
                {{
                    _current{feature.Type.Name} = value;
                }}")}
                else
                {{
                    ExtraFeatureSet(key, value);
                }}
            }}
        }}

        void IFeatureCollection.Set<TFeature>(TFeature feature) 
        {{{Each(allFeatures, feature => $@"
            {(feature.Index != 0 ? "else " : "")}if (typeof(TFeature) == typeof({feature.Type.Name}))
            {{
                _current{feature.Type.Name} = feature;
            }}")}
            else
            {{
                ExtraFeatureSet(typeof(TFeature), feature);
            }}
        }}

        TFeature IFeatureCollection.Get<TFeature>()
        {{
            TFeature feature;{Each(allFeatures, feature => $@"
            {(feature.Index != 0 ? "else " : "")}if (typeof(TFeature) == typeof({feature.Type.Name}))
            {{
                feature = (TFeature)_current{feature.Type.Name};
            }}")}
            else
            {{
                feature = (TFeature)(ExtraFeatureGet(typeof(TFeature)));
            }}

            if (feature != null)
            {{
                return feature;
            }}

            return (TFeature)ConnectionFeatures[typeof(TFeature)];
        }}

        private IEnumerable<KeyValuePair<Type, object>> FastEnumerable()
        {{{Each(allFeatures, feature => $@"
            if (_current{feature.Type.Name} != null)
            {{
                yield return new KeyValuePair<Type, object>({feature.Type.Name}Type, _current{feature.Type.Name} as {feature.Type.Name});
            }}")}

            if (MaybeExtra != null)
            {{
                foreach(var item in MaybeExtra)
                {{
                    yield return item;
                }}
            }}
        }}
    }}
}}
";
        }
    }
}
