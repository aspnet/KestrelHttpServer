using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.Kestrel.Https
{
    public delegate bool ClientCertificateValidationCallback(
        X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors);
}
