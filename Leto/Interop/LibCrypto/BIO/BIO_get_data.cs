using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Leto.Interop
{
    public static partial class LibCrypto
    {
        [DllImport(Libraries.LibCrypto, CallingConvention = CallingConvention.Cdecl, EntryPoint = nameof(BIO_get_data))]
        private unsafe static extern IntPtr Internal_BIO_get_data(BIO a);

        public static unsafe GCHandle BIO_get_data(BIO bio)
        {
            var ptr = Internal_BIO_get_data(bio);

            if (ptr == IntPtr.Zero) return default;

            return GCHandle.FromIntPtr(ptr);
        }

        public static unsafe ref T BIO_get_data<T>(BIO bio) where T : struct
        {
            var ptr = Internal_BIO_get_data(bio);
            return ref Unsafe.AsRef<T>(ptr.ToPointer());
        }
    }
}
