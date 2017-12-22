using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Leto.Interop
{
    public static partial class LibCrypto
    {
        public struct BIO
        {
            private IntPtr _pointer;

            public void Free()
            {
                if(_pointer != IntPtr.Zero)
                {
                    BIO_free(this);
                    _pointer = IntPtr.Zero;
                }
            }

            public override bool Equals(object obj)
            {
                if(obj is BIO bio)
                {
                    return this == bio;
                }
                return false;
            }

            public override int GetHashCode() => _pointer.GetHashCode();

            public static bool operator ==(BIO left, BIO right) => left._pointer == right._pointer;

            public static bool operator !=(BIO left, BIO right) => !(left == right);
        }
    }
}
