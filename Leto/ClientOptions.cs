using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Leto
{
    public class ClientOptions
    {
        public delegate X509Certificate2 GetClientCertificateDelegate(TlsClientPipeline sender);

        public GetClientCertificateDelegate ClientCertificateCallback { get; set; }


    }
}
