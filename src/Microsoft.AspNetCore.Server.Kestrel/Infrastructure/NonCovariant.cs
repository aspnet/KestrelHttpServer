

namespace Microsoft.AspNetCore.Server.Kestrel.Infrastructure
{
    /// <summary>
    /// A simple struct to wrap reference types inside when storing in arrays 
    /// or data structures based on arrays to bypass the CLR's covariant checks when writing to arrays.
    /// See http://codeblog.jonskeet.uk/2013/06/22/array-covariance-not-just-ugly-but-slow-too/
    /// Uses https://github.com/dotnet/corefx/blob/master/src/System.Collections.Immutable/src/System/Collections/Immutable/RefAsValueType.cs
    /// and https://github.com/dotnet/corefxlab/issues/614#issuecomment-184892677
    /// </summary>
    public struct NonCovariant<T> where T : class
    {
        public readonly T Reference;

        private NonCovariant(T value)
        {
            this.Reference = value;
        }

        public static implicit operator NonCovariant<T>(T value)
        {
            return new NonCovariant<T>(value);
        }

        public static implicit operator T(NonCovariant<T> value)
        {
            return value.Reference;
        }
    }
}
