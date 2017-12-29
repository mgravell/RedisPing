using System.Runtime.InteropServices;
using static Leto.Interop.LibCrypto;

namespace Leto.Interop
{
    public static partial class OpenSsl
    {
        [DllImport(Libraries.LibSsl, CallingConvention = CallingConvention.Cdecl, EntryPoint =nameof(SSL_use_PrivateKey))]
        private unsafe extern static int Internal_SSL_use_PrivateKey(SSL ctx, EVP_PKEY pkey);

        public static void SSL_use_PrivateKey(SSL ssl, EVP_PKEY key)
        {
            var result = Internal_SSL_use_PrivateKey(ssl, key);
            ThrowOnErrorReturnCode(result);
        }
    }
}
