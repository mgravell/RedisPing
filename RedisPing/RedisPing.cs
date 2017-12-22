using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipelines.Networking.Sockets;
using System.IO.Pipelines.Text.Primitives;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;

static class RedisPing
{
    static async Task Main()
    {
        // need to put these somewhere that isn't the repo; maybe a json file that
        // is in the .gitignore?
        int port = 6379;
        string host = "localhost";
        string password = null; // <=== azure auth key
        bool useTls = false;

        // ***TODO***: put azure details here:
        // host = "your-azure-endpoint.redis.cache.windows.net";
        // port = 6380;
        // password = "your access key";
        // useTls = true;

        await Console.Error.WriteLineAsync("*** via TcpClient ***");
        await DoTheThingViaTcpClient(host, port, password, useTls);

        await Console.Error.WriteLineAsync();
        await Console.Error.WriteLineAsync();

        await Console.Error.WriteLineAsync("*** via raw pipelines ***");
        await DoTheThingViaPipelines(host, port, password, useTls);
    }

    static async Task DoTheThingViaTcpClient(string host, int port, string password, bool useTls)
    {
        try
        {
            using (var pool = new MemoryPool())
            using (var client = new TcpClient())
            {
                await Console.Out.WriteLineAsync($"connecting to {host}:{port}...");
                await client.ConnectAsync(host, port);
                Stream stream = client.GetStream();

                if(useTls)
                {
                    await Console.Out.WriteLineAsync($"authenticating host...");
                    var ssl = new SslStream(stream);
                    await ssl.AuthenticateAsClientAsync(host);
                    stream = ssl;
                }

                using (var pipe = new StreamPipeConnection(new PipeOptions(pool), stream))
                {
                    await Execute(pipe, password);
                }
            }
        }
        catch(Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
        }
    }

    private static async Task Execute(IPipeConnection connection, string password)
    {
        await Console.Out.WriteLineAsync($"executing...");

        if (password != null)
        {
            await WriteSimpleMessage(connection.Output, $"AUTH \"{password}\"");
            // a "success" for this would be a response that says "+OK"
        }

        await WriteSimpleMessage(connection.Output, "ECHO \"noisy in here\"");
        // note that because of RESP, this actually gives 2 replies; don't worry about it :)

        await WriteSimpleMessage(connection.Output, "PING");


        var input = connection.Input;
        while (true)
        {
            await Console.Out.WriteLineAsync($"awaiting response...");
            var result = await input.ReadAsync();

            await Console.Out.WriteLineAsync($"checking response...");
            var buffer = result.Buffer;

            if (buffer.IsEmpty && result.IsCompleted)
            {
                await Console.Out.WriteLineAsync($"done");
                break;
            }

            if (!buffer.TrySliceTo((byte)'\r', (byte)'\n', out var slice, out var cursor))
            {
                await Console.Out.WriteLineAsync($"incomplete");
                input.Advance(buffer.Start, buffer.End);
                continue;
            }
            var reply = slice.GetAsciiString();
            await Console.Out.WriteLineAsync($"<< received: '{reply}'");
            if (string.Equals(reply, "+PONG", StringComparison.OrdinalIgnoreCase))
            {
                await WriteSimpleMessage(connection.Output, "QUIT");
                connection.Output.Complete();
            }

            // input.Advance(cursor); // feels like this should work, but it doesn't 
            var incTerminator = buffer.Slice(0, slice.Length + 2);
            input.Advance(incTerminator.End, incTerminator.End);

        }
    }

    static async Task DoTheThingViaPipelines(string host, int port, string password, bool useTls)
    {
        try
        {


            await Console.Out.WriteLineAsync($"resolving ip of '{host}'...");
            var ip = (await Dns.GetHostAddressesAsync(host)).First();

            await Console.Out.WriteLineAsync($"connecting to '{ip}:{port}'...");
            using (var connection = await SocketConnection.ConnectAsync(new IPEndPoint(ip, port)))
            {
                if (useTls)
                {
                    throw new NotImplementedException("some pipe wrapping goes here?");
                }

                await Execute(connection, password);
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
        }
    }

    private static async Task WriteSimpleMessage(IPipeWriter output, string command)
    {
        // keep things simple: using the text protocol guarantees a text-protocol response
        // (in real code we'd use the RESP format, but ... meh)
        await Console.Out.WriteLineAsync($">> sending '{command}'...");
        var buffer = output.Alloc();

        buffer.WriteUtf8(command.AsReadOnlySpan());
        buffer.Write(CRLF);
        buffer.Commit();
        await buffer.FlushAsync();
    }

    static readonly byte[] CRLF = { (byte)'\r', (byte)'\n' };

    private static int WriteUtf8(ref this WritableBuffer buffer, string value)
        => buffer.WriteUtf8(value.AsReadOnlySpan());
    private static int WriteUtf8(ref this WritableBuffer buffer, ReadOnlySpan<char> value)
    {
               
        if (value.IsEmpty) return 0;

        int totalWritten = 0;
        var source = value.AsBytes();
        do
        {
            buffer.Ensure(4); // be able to write at least one character (worst case) - but the span obtained could be much bigger
            var status = Encodings.Utf8.FromUtf16(source, buffer.Buffer.Span, out int bytesConsumed, out int bytesWritten);
            switch(status)
            {
                case OperationStatus.Done:
                case OperationStatus.DestinationTooSmall:
                    if (bytesWritten == 0) ThrowInvalid("Zero bytes encoded");

                    buffer.Advance(bytesWritten);
                    source = source.Slice(bytesConsumed);
                    totalWritten += bytesWritten;
                    break;
                default:
                    ThrowInvalid($"Unexpected encoding status: {status}");
                    break;
            }
        } while (!source.IsEmpty);
        return totalWritten;        
    }
    static void ThrowInvalid(string message) => throw new InvalidOperationException(message);
}
