using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.Kestrel.Https
{
    public enum ClientCertificateMode
    {
        NoCertificate,
        AllowCertificate,
        RequireCertificate
    }
}
