using System;
using System.IO.Pipelines;
using System.IO.Pipelines.Text.Primitives;

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
        public string AsString() => _payload.GetUtf8Span();

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
                    int len = checked((int)ReadableBufferExtensions.GetUInt32(payload));
                    //int len = ParseInt32(payload);
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

    }
}
