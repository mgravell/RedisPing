using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Newtonsoft.Json;
using RedisPing;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipelines.Text.Primitives;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace RedisPing
{
    class TestCase
    {
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Password { get; set; }
        public bool UseTls { get; set; }
        public string CertificatePath { get; set; }
    }
    static class Program
    {
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
            var testRoot = "Tests";
            using (var pool = new MemoryPool())
            {
                foreach (var path in Directory.EnumerateFiles(testRoot, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        var test = JsonConvert.DeserializeObject<TestCase>(json);
                        X509Certificate cert = null;
                        string certPath = null;
                        if (test.CertificatePath != null)
                        {
                            certPath = Path.Combine(testRoot, test.CertificatePath);
                            cert = new X509Certificate2(certPath);
                        }

                        await Console.Out.WriteLineAsync($"Test: {test.Name ?? test.Host}");
                        if (cert != null)
                        {
                            await Console.Out.WriteLineAsync($"Client certificate: {cert.Subject}");
                        }

                        await Console.Out.WriteLineAsync("via TcpClient...");
                        await DoTheThingViaTcpClient(pool, test.Host, test.Port, test.Password, test.UseTls, cert);
                        await Console.Out.WriteLineAsync();

                        await Console.Out.WriteLineAsync("via Pipelines...");
                        await DoTheThingViaPipelines(pool, test.Host, test.Port, test.Password, test.UseTls, certPath);
                        await Console.Out.WriteLineAsync();
                        await Console.Out.WriteLineAsync();
                    }
                    catch (Exception ex)
                    {
                        await Console.Error.WriteLineAsync($"Error processing '{path}': '{ex.Message}'");
                    }
                }
            }
        }

        static async Task DoTheThingViaTcpClient(MemoryPool pool, string host, int port, string password, bool useTls, X509Certificate cert)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    await Console.Out.WriteLineAsync(ShowDetails ? $"connecting to {host}:{port}..." : "connecting to host");
                    await client.ConnectAsync(host, port);
                    Stream stream = client.GetStream();

                    if (useTls)
                    {
                        await Console.Out.WriteLineAsync($"authenticating host...");
                        LocalCertificateSelectionCallback certSelector = null;
                        if (cert != null)
                        {
                            certSelector = delegate { return cert; };
                        }
                        RemoteCertificateValidationCallback serverValidator = delegate { return true; }; // WCGW?
                        var ssl = new SslStream(stream, false, serverValidator, certSelector);
                        await ssl.AuthenticateAsClientAsync(host);
                        if (ssl.LocalCertificate != null)
                        {
                            await Console.Out.WriteLineAsync($"Local cert: {ssl.LocalCertificate.Subject}");
                        }
                        stream = ssl;
                    }

                    using (var pipe = new StreamPipeConnection(new PipeOptions(pool), stream))
                    {
                        await ExecuteWithTimeout(pipe, password);
                    }
                }
            }
            catch (Exception ex)
            {
                while (ex != null)
                {
                    await Console.Error.WriteLineAsync(ex.Message);
                    ex = ex.InnerException;
                }
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

            // send some messages down the wire...

            if (password != null)
            {
                // a "success" for this would be a response that says "+OK"
                await WriteSimpleMessage(connection.Output, $"AUTH \"{password}\"");
            }
            await WriteSimpleMessage(connection.Output, "ECHO \"noisy in here\"");
            await WriteSimpleMessage(connection.Output, "PING");

            // now process the responses
            await RunMainParserLoop(connection);
        }
        private static async Task RunMainParserLoop(IPipeConnection connection)
        {
            var input = connection.Input;
            while (true) // outer loop is successive calls to ReadAsync
            {
                await Console.Out.WriteLineAsync($"awaiting response...");
                var result = await input.ReadAsync();

                var buffer = result.Buffer;
                if (buffer.IsEmpty && result.IsCompleted)
                {
                    await Console.Out.WriteLineAsync($"done");
                    break;
                }

                // we could have 0/1/many (or fractional) frames in the input buffer; try to consume
                // whatever we can
                bool haveAnyFrame = false;
                await Console.Out.WriteLineAsync($"checking response ({buffer.Length} bytes)...");
                while (!buffer.IsEmpty && RespReply.TryParse(buffer, out var response, out var end))
                {
                    haveAnyFrame = true;

                    // process the frame that we received; nothing has been allocated so far - we just
                    // have placeholders to the structured data in the memory pool
                    // note that since this data isn't "preserved", it is only valid until we
                    // call .Advance to move past it (releasing the chunks)
                    await ProcessResponse(connection.Output, response);

                    // we read a frame; slice forward and try again
                    buffer = buffer.Slice(end);
                }

                if (!haveAnyFrame) // no frames parsed successfully;
                { // this usually means either large messages or packet fragmentation
                    await Console.Out.WriteLineAsync($"incomplete");
                }

                // we have *consumed* up to the current start of buffer, and
                // we have *inspeccted* to the end of the buffer; note that this
                // is how the back-buffer of unprocessed data is managed automatically
                // for us - it releases everything that we say has been consumed; anything
                // beyond that will be offered up again
                input.Advance(buffer.Start, buffer.End);
            }
        }

        private static async Task ProcessResponse(IPipeWriter output, RespReply response)
        {
            var reply = response.ToString();
            await Console.Out.WriteLineAsync($"<< received: '{reply}' ({response.Type})");
            if (response.Type == MessageType.SimpleString &&
                string.Equals(reply, "PONG", StringComparison.OrdinalIgnoreCase))
            {
                await WriteSimpleMessage(output, "QUIT");
                output.Complete();
            }
        }

        static async Task DoTheThingViaPipelines(MemoryPool pool, string host, int port, string password, bool useTls, string certificateFile)
        {
            try
            {
                await Console.Out.WriteLineAsync(ShowDetails ? $"connecting to '{host}:{port}'..." : "connecting to host");
                using (var socket = await SocketTransportFactory.ConnectAsync(new DnsEndPoint(host, port), pool))
                {
                    IPipeConnection connection = socket;
                    if (useTls) // need to think about the disposal story here?
                    {
                        await Console.Out.WriteLineAsync("authenticating client...");
                        connection = await Leto.TlsPipeline.AuthenticateClient(connection, new Leto.ClientOptions
                        { CertificateFile = certificateFile });
                        await Console.Out.WriteLineAsync("authenticated");
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
            // keep things simple: using the text protocol
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
                switch (status)
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
}