using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Leto.Interop
{
    public static partial class OpenSsl
    {
        public struct SSL_METHOD
        {
            private IntPtr _pointer;

            public IntPtr Pointer { get => _pointer; set => _pointer = value; }

            public override bool Equals(object obj)
            {
                if (obj is SSL_METHOD method)
                {
                    return this == method;
                }
                return false;
            }

            public override int GetHashCode() => _pointer.GetHashCode();

            public static bool operator ==(SSL_METHOD left, SSL_METHOD right) => left._pointer == right._pointer;

            public static bool operator !=(SSL_METHOD left, SSL_METHOD right) => !(left == right);
        }

        [DllImport(Libraries.LibSsl, CallingConvention = CallingConvention.Cdecl)]
        public static extern SSL_METHOD TLS_server_method();

        [DllImport(Libraries.LibSsl, CallingConvention = CallingConvention.Cdecl)]
        public static extern SSL_METHOD TLS_client_method();
    }
}
