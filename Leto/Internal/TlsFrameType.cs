using System;
using System.Collections.Generic;
using System.Text;

namespace Leto.Internal
{
    public enum TlsFrameType : byte
    {
        ChangeCipherSpec = 20,
        Alert = 21,
        Handshake = 22,
        AppData = 23,
        Invalid = 255,
        Incomplete = 0,
    }
}
