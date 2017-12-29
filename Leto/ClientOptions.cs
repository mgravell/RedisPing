using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Leto
{
    public class ClientOptions
    {
        public string CertificatePassword { internal get; set; }
        public string CertificateFile { get; set; }
    }
}
