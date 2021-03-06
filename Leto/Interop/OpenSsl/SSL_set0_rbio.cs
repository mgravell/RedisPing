using System.Runtime.InteropServices;
using static Leto.Interop.LibCrypto;

namespace Leto.Interop
{
    public static partial class OpenSsl
    {
        [DllImport(Libraries.LibSsl, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SSL_set0_rbio(SSL ssl, BIO rbio);
    }
}
