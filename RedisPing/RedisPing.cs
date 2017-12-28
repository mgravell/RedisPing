using Newtonsoft.Json;
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
    class TestCase
    {
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Password { get; set; }
        public bool UseTls { get; set; }
    }
    private static bool ShowDetails
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
    static async Task Main()
    {
        foreach(var path in Directory.EnumerateFiles("Tests", "*.json"))
        {
            try
            {
                var json = File.ReadAllText(path);
                var test = JsonConvert.DeserializeObject<TestCase>(json);
                await Console.Out.WriteLineAsync($"Test: {test.Name ?? test.Host}");

                await Console.Error.WriteLineAsync("via TcpClient...");
                await DoTheThingViaTcpClient(test.Host, test.Port, test.Password, test.UseTls);
                await Console.Error.WriteLineAsync();

                await Console.Error.WriteLineAsync("via Pipelines...");
                await DoTheThingViaPipelines(test.Host, test.Port, test.Password, test.UseTls);
                await Console.Error.WriteLineAsync();
                await Console.Error.WriteLineAsync();
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error processing '{path}': '{ex.Message}'");
            }
        }
    }

    static async Task DoTheThingViaTcpClient(string host, int port, string password, bool useTls)
    {
        try
        {
            using (var pool = new MemoryPool())
            using (var client = new TcpClient())
            {
                await Console.Out.WriteLineAsync(ShowDetails ? $"connecting to {host}:{port}..." : "connecting to host");
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
                    await ExecuteWithTimeout(pipe, password);
                }
            }
        }
        catch(Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
        }
    }
    private static async Task ExecuteWithTimeout(IPipeConnection connection, string password, int timeoutMilliseconds = 5000)
    {
        var timeout = Task.Delay(timeoutMilliseconds);
        var success = Execute(connection, password);
        var winner = await Task.WhenAny(success, timeout);
        await Console.Out.WriteLineAsync(winner == success ? "(complete)" : "(timeout)");
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


            await Console.Out.WriteLineAsync(ShowDetails ? $"resolving ip of '{host}'..." : "resolving ip of host");
            var ip = (await Dns.GetHostAddressesAsync(host)).First();

            await Console.Out.WriteLineAsync(ShowDetails ? $"connecting to '{ip}:{port}'..." : "connecting to host");
            using (var socket = await SocketConnection.ConnectAsync(new IPEndPoint(ip, port)))
            {
                IPipeConnection connection = socket;
                if (useTls) // need to think about the disposal story here?
                {
                    connection = await Leto.TlsPipeline.AuthenticateClient(connection, new Leto.ClientOptions());
                }
                await ExecuteWithTimeout(connection, password);
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
        var msg = (!ShowDetails && command.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase)) ? "AUTH ****" : command;
        await Console.Out.WriteLineAsync($">> sending '{msg}'...");
        var buffer = output.Alloc();

        buffer.WriteUtf8(command.AsReadOnlySpan());
        buffer.Ensure(CRLF.Length);
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
