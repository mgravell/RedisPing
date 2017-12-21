using System;
using System.IO.Pipelines;
using System.IO.Pipelines.Networking.Sockets;
using System.IO.Pipelines.Text.Primitives;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

static class RedisPing
{
    static async Task Main()
    {
        try
        {
            // need to put these somewhere that isn't the repo; maybe a json file that
            // is in the .gitignore?
            const int RedisPort = 6379;
            string host = "localhost";
            string password = null; // <=== azure auth key

            await Console.Out.WriteLineAsync($"resolving ip of '{host}'...");
            var ip = (await Dns.GetHostAddressesAsync(host)).First();

            

            

            await Console.Out.WriteLineAsync($"connecting to '{ip}:{RedisPort}'...");
            using (var connection = await SocketConnection.ConnectAsync(new IPEndPoint(ip, RedisPort)))
            {
                if(password != null)
                {
                    await WriteSimpleMessage(connection.Output, $"AUTH {password}");
                    // a "success" for this would be a response that says "+OK"
                }

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
                    await Console.Out.WriteLineAsync($"received: '{reply}'");
                    if(string.Equals(reply, "+PONG", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteSimpleMessage(connection.Output, "QUIT");
                        connection.Output.Complete();
                    }

                    // input.Advance(cursor); // feels like this should work, but it doesn't 
                    var incTerminator = buffer.Slice(slice.Length + 2);
                    input.Advance(incTerminator.End);

                }
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
        await Console.Out.WriteLineAsync($"sending '{command}'...");
        var buffer = output.Alloc();
        
        var arr = Encoding.ASCII.GetBytes($"{command}\r\n"); // there's a nice way to do this; I've forgotten
        buffer.Write(arr);
        await buffer.FlushAsync();
    }
}
