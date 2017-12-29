using System.Runtime.InteropServices;
using static Leto.Interop.LibCrypto;

namespace Leto.Interop
{
    public static partial class OpenSsl
    {
        [DllImport(Libraries.LibSsl, CallingConvention = CallingConvention.Cdecl, EntryPoint = nameof(SSL_use_certificate))]
        private unsafe extern static int Internal_SSL_use_certificate(SSL ctx, X509 cert);

        public static void SSL_use_certificate(SSL ssl, X509 cert)
        {
            var result = Internal_SSL_use_certificate(ssl, cert);
            ThrowOnErrorReturnCode(result);
        }
    }
}
