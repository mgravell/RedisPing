using System.Runtime.InteropServices;

namespace Leto.Interop
{
    public static partial class OpenSsl
    {
        [DllImport(Libraries.LibSsl, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SSL_pending(SSL ssl);
    }
}
