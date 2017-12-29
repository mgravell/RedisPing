using Leto.Internal;
using Leto.Interop;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static Leto.Interop.LibCrypto;
using static Leto.Interop.OpenSsl;

namespace Leto
{
    public class TlsClientPipeline : IPipeConnection, IDisposable
    {
        private SSL_CTX _context;
        private SSL _ssl;
        private ClientOptions _clientOptions;
        private Pipe _readInnerPipe;
        private Pipe _writeInnerPipe;
        private IPipeConnection _innerConnection;
        private BIO _readBio;
        private BIO _writeBio;
        private TaskCompletionSource<bool> _handshakeTask;
        private bool _handshakeComplete;

        private static readonly CustomReadBioDescription s_ReadBio = new CustomReadBioDescription();
        private static readonly CustomWriteBioDescription s_WriteBio = new CustomWriteBioDescription();

        internal TlsClientPipeline(IPipeConnection inputPipe, SSL_CTX context, ClientOptions clientOptions)
        {
            _innerConnection = inputPipe;
            _readInnerPipe = new Pipe(new PipeOptions(System.Buffers.MemoryPool.Default));
            _writeInnerPipe = new Pipe(new PipeOptions(System.Buffers.MemoryPool.Default));
            _context = context;
            _ssl = SSL_new(_context);
            _clientOptions = clientOptions;

            _readBio = s_ReadBio.New();
            _writeBio = s_WriteBio.New();
            SSL_set0_rbio(_ssl, _readBio);
            SSL_set0_wbio(_ssl, _writeBio);
            SSL_set_connect_state(_ssl);
        }

        internal IPipeConnection InnerConnection => _innerConnection;

        private async Task HandshakeLoop()
        {
            await ProcessHandshakeMessage(default, _innerConnection.Output);

            try
            {
                while (true)
                {
                    var readResult = await _innerConnection.Input.ReadAsync();
                    var buffer = readResult.Buffer;
                    try
                    {
                        if (buffer.IsEmpty && readResult.IsCompleted)
                        {
                            _handshakeTask.SetException(new InvalidOperationException("Failed to complete handshake"));
                            return;
                        }

                        while (TryGetFrame(ref buffer, out ReadableBuffer messageBuffer, out TlsFrameType frameType))
                        {
                            if (frameType != TlsFrameType.Handshake && frameType != TlsFrameType.ChangeCipherSpec)
                            {
                                _handshakeTask.SetException(new InvalidOperationException($"Received an invalid frame for the current handshake state {frameType}"));
                                return;
                            }

                            await ProcessHandshakeMessage(messageBuffer, _innerConnection.Output);

                            if (_handshakeTask.Task.IsCompletedSuccessfully)
                            {
                                return;
                            }
                        }
                    }
                    finally
                    {
                        _innerConnection.Input.Advance(buffer.Start, buffer.End);
                    }
                }
            }
            finally
            {
                if (_handshakeTask.Task.IsCompletedSuccessfully)
                {
                    var ignore = StartReading();
                    ignore = StartWriting();
                }
            }
        }

        private async Task StartWriting()
        {
            await _handshakeTask.Task.ConfigureAwait(false);

            var maxBlockSize = (int)Math.Pow(2, 14);
            try
            {
                while (true)
                {
                    var result = await _writeInnerPipe.Reader.ReadAsync();

                    var buffer = result.Buffer;
                    if (buffer.IsEmpty && result.IsCompleted)
                    {
                        break;
                    }

                    try
                    {
                        while (buffer.Length > 0)
                        {
                            ReadableBuffer messageBuffer;
                            if (buffer.Length <= maxBlockSize)
                            {
                                messageBuffer = buffer;
                                buffer = buffer.Slice(buffer.End);
                            }
                            else
                            {
                                messageBuffer = buffer.Slice(0, maxBlockSize);
                                buffer = buffer.Slice(maxBlockSize);
                            }

                            await EncryptAsync(messageBuffer, _innerConnection.Output);
                        }
                    }
                    finally
                    {
                        _writeInnerPipe.Advance(buffer.End);
                    }
                }
            }
            finally
            {
                // Need to shut down the channels properly not sure how this should occur
            }
        }

        private async Task StartReading()
        {
            try
            {
                while (true)
                {
                    var result = await _innerConnection.Input.ReadAsync();
                    var buffer = result.Buffer;
                    try
                    {
                        if (buffer.IsEmpty && result.IsCompleted)
                        {
                            break;
                        }

                        while (TryGetFrame(ref buffer, out ReadableBuffer messageBuffer, out TlsFrameType frameType))
                        {
                            if (frameType != TlsFrameType.AppData)
                            {
                                // Throw we don't support renegotiation at this point
                                throw new InvalidOperationException($"Invalid frame type {frameType} expected app data");
                            }

                            await DecryptAsync(messageBuffer, _readInnerPipe);
                        }
                    }
                    finally
                    {
                        _innerConnection.Input.Advance(buffer.Start, buffer.End);
                    }
                }
            }
            finally
            {

            }
        }

        private WritableBufferAwaitable EncryptAsync(ReadableBuffer unencrypted, IPipeWriter writer)
        {
            var handle = GCHandle.Alloc(writer);
            try
            {
                BIO_set_data(_writeBio, handle);
                while (unencrypted.Length > 0)
                {
                    var totalWritten = SSL_write(_ssl, unencrypted.First.Span);
                    unencrypted = unencrypted.Slice(totalWritten);
                }

                return writer.Alloc().FlushAsync();
            }
            finally
            {
                handle.Free();
            }
        }

        private WritableBufferAwaitable DecryptAsync(ReadableBuffer messageBuffer, Pipe readInnerPipe)
        {
            var decryptedData = readInnerPipe.Writer.Alloc();
            BIO_set_data(_readBio, ref messageBuffer);
            var result = 1;
            while (result > 0)
            {
                decryptedData.Ensure(1024);

                result = SSL_read(_ssl, decryptedData.Buffer.Span);
                if (result > 0)
                {
                    decryptedData.Advance(result);
                }
            }

            return decryptedData.FlushAsync();
        }

        private async Task ProcessHandshakeMessage(ReadableBuffer readBuffer, IPipeWriter writer)
        {
            var writeHandle = GCHandle.Alloc(writer);
            try
            {
                BIO_set_data(_readBio, ref readBuffer);
                BIO_set_data(_writeBio, writeHandle);

                var result = SSL_do_handshake(_ssl);
                if (result == 1)
                {
                    // handshake is complete
                    _handshakeTask.SetResult(true);
                    return;
                }

                // Not completed, so we need to check if its an error or if we should continue
                var sslResultCode = SSL_get_error(_ssl, result);
                if (sslResultCode == SslErrorCodes.SSL_ASYNC_PAUSED)
                {
                    await writer.Alloc().FlushAsync();
                    return;
                }
                else
                {
                    return;
                }

                // We had some other error need to fill in and figure out how to deal with it.
                throw new NotImplementedException();
            }
            finally
            {
                writeHandle.Free();
            }
        }

        private static bool TryGetFrame(ref ReadableBuffer buffer, out ReadableBuffer messageBuffer, out TlsFrameType frameType)
        {
            frameType = TlsFrameType.Incomplete;

            // The header is 5 bytes long so if it's less than that just exit
            if (buffer.Length < 5)
            {
                messageBuffer = default;
                return false;
            }

            var span = buffer.ToSpan(5);

            frameType = (TlsFrameType)span[0];

            // Check it's a valid frametype for what we are expecting

            if ((byte)frameType < 20 | (byte)frameType > 24)
            {
                // Unknown frametype, error
                throw new FormatException($"The Tls frame type was invalid, type was {frameType}");
            }

            // Get the Tls Version
            var version = span[1] << 8 | span[2];

            if (version < 0x300 || version >= 0x500)
            {
                //Unknown or unsupported message version
                messageBuffer = default;
                throw new FormatException($"The Tls frame version was invalid, the version was {version}");
            }

            var length = span[3] << 8 | span[4];
            if (buffer.Length >= (length + 5))
            {
                messageBuffer = buffer.Slice(0, length + 5);
                buffer = buffer.Slice(messageBuffer.End);
                return true;
            }

            messageBuffer = default;
            return false;
        }

        public Task AuthenticateAsync() => _handshakeTask?.Task ?? StartHandshake();

        private Task StartHandshake()
        {
            var ignore = HandshakeLoop();
            _handshakeTask = new TaskCompletionSource<bool>();
            return _handshakeTask.Task;
        }

        public IPipeReader Input => _readInnerPipe.Reader;
        public IPipeWriter Output => _writeInnerPipe.Writer;

        public void Dispose()
        {
            _context.Close();
            _ssl.Close();
        }
    }
}
