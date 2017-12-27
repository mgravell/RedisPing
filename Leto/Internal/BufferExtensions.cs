using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;

namespace Leto.Internal
{
    internal static class BufferExtensions
    {
        public static ReadOnlySpan<byte> ToSpan(this ReadableBuffer buffer, int length)
        {
            var sliced = buffer.Slice(0, length);
            if(sliced.IsSingleSpan)
            {
                return sliced.First.Span.Slice(0, length);
            }
            var newBuffer = new byte[length];
            sliced.CopyTo(newBuffer);
            return newBuffer;
        }
    }
}
