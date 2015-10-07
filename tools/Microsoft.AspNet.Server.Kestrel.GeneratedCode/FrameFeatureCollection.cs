using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNet.Http.Features;
using Microsoft.Dnx.Compilation.CSharp;
using Microsoft.AspNet.Http.Features.Internal;
using Microsoft.AspNet.Http.Features.Authentication;

namespace Microsoft.AspNet.Server.Kestrel.GeneratedCode
{
    // This project can output the Class library as a NuGet Package.
    // To enable this option, right-click on the project and select the Properties menu item. In the Build tab select "Produce outputs on build".
    public class FrameFeatureCollection : ICompileModule
    {
        static string Each<T>(IEnumerable<T> values, Func<T, string> formatter)
        {
            return values.Select(formatter).Aggregate((a, b) => a + b);
        }

        public virtual void BeforeCompile(BeforeCompileContext context)
        {
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(GeneratedFile());
            context.Compilation = context.Compilation.AddSyntaxTrees(syntaxTree);
        }

        public static string GeneratedFile()
        {
            var alwaysFeatures = new[]
            {
                typeof(IHttpRequestFeature),
                typeof(IHttpResponseFeature),
                typeof(IHttpRequestIdentifierFeature),
                typeof(IServiceProvidersFeature),
                typeof(IHttpRequestLifetimeFeature),
                typeof(IHttpConnectionFeature)
            };

            var commonFeatures = new[]
            {
                typeof(IHttpAuthenticationFeature),
                typeof(IQueryFeature),
                typeof(IFormFeature)
            };

            var sometimesFeatures = new[]
            {
                typeof(IHttpUpgradeFeature),
                typeof(IResponseCookiesFeature),
                typeof(IItemsFeature),
                typeof(ITlsConnectionFeature),
                typeof(IHttpWebSocketFeature),
                typeof(ISessionFeature)
            };

            var rareFeatures = new[]
            {
                typeof(IHttpSendFileFeature)
            };

            var allFeatures = alwaysFeatures.Concat(commonFeatures).Concat(sometimesFeatures).Concat(rareFeatures);

            // NOTE: This list MUST always match the set of feature interfaces implemented by Frame.
            // See also: src/Microsoft.AspNet.Server.Kestrel/Http/Frame.FeatureCollection.cs
            var implementedFeatures = new[]
            {
                typeof(IHttpRequestFeature),
                typeof(IHttpResponseFeature),
                typeof(IHttpUpgradeFeature),
            };

            var cachedFeatures = alwaysFeatures.Concat(commonFeatures).Where(f => !implementedFeatures.Contains(f));
            
            return $@"
using System;
using System.Collections.Generic;

namespace Microsoft.AspNet.Server.Kestrel.Http 
{{
    public partial class Frame
    {{{Each(implementedFeatures.Select((feature, index) => new { feature, index }), entry => $@"
        private const int flag{entry.feature.Name} = {1 << entry.index};")}
        {Each(allFeatures, feature => $@"
        private static readonly Type {feature.Name}Type = typeof(global::{feature.FullName});")}
        {Each(cachedFeatures, feature => $@"
        private object _current{feature.Name};")}

        private int _featureOverridenFlags = 0;

        private void FastReset()
        {{
            _featureOverridenFlags = 0;
            {Each(cachedFeatures, feature => $@"
            _current{feature.Name} = null;")}
        }}

        private object FastFeatureGet(Type key)
        {{{Each(implementedFeatures, feature => $@"
            if (key == {feature.Name}Type)
            {{
                if ((_featureOverridenFlags & flag{feature.Name}) == 0)
                {{
                    return this;
                }}
                return SlowFeatureGet(key);
            }}")}
            {Each(cachedFeatures, feature => $@"
            if (key == {feature.Name}Type)
            {{
                return _current{feature.Name};
            }}")}
            return  SlowFeatureGet(key);
        }}

        private object SlowFeatureGet(Type key)
        {{
            if (MaybeExtra == null) 
            {{
                return null;
            }}
            for (var i = 0; i < MaybeExtra.Count; i++)
            {{
                var kv = MaybeExtra[i];
                if (kv.Key == key)
                {{
                    return kv.Value;
                }}
            }}
            return null;
        }}

        private void FastFeatureSetInner(int flag, Type key, object feature)
        {{
            SetExtra(key, feature);
            _featureOverridenFlags |= flag;
        }}

        private void FastFeatureSet(Type key, object feature)
        {{
            _featureRevision++;
            {Each(implementedFeatures, feature => $@"
            if (key == {feature.Name}Type)
            {{
                FastFeatureSetInner(flag{feature.Name}, key, feature);
                return;
            }}")};
            {Each(cachedFeatures, feature => $@"
            if (key == {feature.Name}Type)
            {{
                _current{feature.Name} = feature;
                return;
            }}")};
            SetExtra(key, feature);
        }}

        private IEnumerable<KeyValuePair<Type, object>> FastEnumerable()
        {{{Each(implementedFeatures, feature => $@"
            if ((_featureOverridenFlags & flag{feature.Name}) == 0)
            {{
                yield return new KeyValuePair<Type, object>({feature.Name}Type, this as global::{feature.FullName});
            }}
            else
            {{
                var feature = SlowFeatureGet({feature.Name}Type);
                if (feature != null)
                {{
                    yield return new KeyValuePair<Type, object>({feature.Name}Type, feature as global::{feature.FullName});
                }}
            }}")};

            {Each(cachedFeatures, feature => $@"
            if (_current{feature.Name} != null)
            {{
                yield return new KeyValuePair<Type, object>({feature.Name}Type, _current{feature.Name} as global::{feature.FullName});
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

        public virtual void AfterCompile(AfterCompileContext context)
        {
        }
    }
}
