using System;
using System.IO.Pipelines;
using System.Text;

namespace RedisPing
{
    enum MessageType : byte
    {
        SimpleString = (byte)'+',
        Error = (byte)'-',
        Integer = (byte)':',
        BulkString = (byte)'$',
        Array = (byte)'*',
    }
    struct RespReply
    {
        public MessageType Type { get; }
        private ReadableBuffer _payload;
        private RespReply[] _subItems;

        public override string ToString() => AsString();
        public string AsString() // => _payload.GetUtf8Span();
        {
            var tmp = _payload;
            if (tmp.IsEmpty) return "";

            // this is horribly inefficient; where have all the good string functions gone?
            var memory = tmp.IsSingleSpan ? tmp.First : tmp.ToArray();
            if(!memory.TryGetArray(out var segment))
            {
                segment = new ArraySegment<byte>(memory.ToArray());
            }
            return Encoding.UTF8.GetString(segment.Array, segment.Offset, segment.Count);
        }
        
        private RespReply(MessageType type, ReadableBuffer payload, RespReply[] subItems = null)
        {   // note that **these buffers are not preserved**; they cannot be used once we have advanced
            Type = type;
            _payload = payload;
            _subItems = subItems;
        }
        
        private static bool TrySliceToCrLf(ReadableBuffer value, out ReadableBuffer slice, out ReadCursor end)
        {
            bool success = value.TrySliceTo((byte)'\r', (byte)'\n', out slice, out _);
            // the cursor from TrySliceTo is the CRLF - we want to return the *after* position
            end = success ? value.Slice(slice.Length + 2).Start : default;
            return success;
        }
        static readonly Memory<byte> CRLF = new byte[] { (byte)'\r', (byte)'\n' };

        public static bool TryParse(ReadableBuffer buffer, out RespReply value, out ReadCursor end)
        {
            end = default;
            value = default;
            if (buffer.IsEmpty) return false;

            // RESP type is denoted by prefix char
            var type = (MessageType)buffer.First.Span[0];
            buffer = buffer.Slice(1); // consumed

            switch (type)
            {
                case MessageType.SimpleString:
                case MessageType.Error:
                case MessageType.Integer:
                    // simple value followed by CRLF
                    if (!TrySliceToCrLf(buffer, out var payload, out end)) return false;
                    value = new RespReply(type, payload);
                    return true;
                case MessageType.BulkString:
                    // length as ASCII followed by CRLF, then that many bytes, then CRLF
                    if (!TrySliceToCrLf(buffer, out payload, out end)) return false;
                    int len = ParseInt32(payload);

                    buffer = buffer.Slice(end);
                    if (buffer.Length < len + 2) return false; // payload+CRLF bytes not present

                    payload = buffer.Slice(0, len);
                    var crlf = buffer.Slice(len, 2);
                    if (!crlf.EqualsTo(CRLF.Span)) throw new FormatException("Missing terminator after bulk-string");
                    end = crlf.End;
                    value = new RespReply(type, payload);
                    return true;
                case MessageType.Array:
                    // haven't needed yet for my test scenario
                    throw new NotImplementedException(type.ToString());
                default:
                    throw new InvalidProgramException($"Unknown message prefix: {(char)type}");
            }
        }

        private static int ParseInt32(ReadableBuffer payload)
        {
            int Combine(int value, Span<byte> chars)
            {
                for(int i = 0; i < chars.Length; i++)
                {
                    int digit = chars[i] - (byte)'0';
                    if (digit < 0 || digit > 9) throw new FormatException();
                    value = checked((value * 10) + digit);
                }
                return value;
            }

            if (payload.IsEmpty) throw new FormatException();
            int result = 0;
            if (payload.IsSingleSpan)
            {
                result = Combine(result, payload.First.Span);
            }
            else
            {
                foreach(var range in payload)
                {
                    result = Combine(result, range.Span);
                }
            }
            return result;
        }
    }
}
