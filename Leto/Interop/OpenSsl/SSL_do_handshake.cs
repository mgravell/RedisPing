using System.Runtime.InteropServices;

namespace Leto.Interop
{
    public static partial class OpenSsl
    {
        [DllImport(Libraries.LibSsl, CallingConvention = CallingConvention.Cdecl)]
        public extern static int SSL_do_handshake(SSL ssl);
    }
}
