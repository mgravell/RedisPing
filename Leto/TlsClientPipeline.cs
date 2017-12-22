using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;
using static Leto.Interop.OpenSsl;

namespace Leto
{
    public class TlsClientPipeline : IPipe, IDisposable
    {
        private SSL_CTX _context;
        private SSL _ssl;
        private ClientOptions _clientOptions;
        private Pipe _innerPipe;
        private IPipe _connectionPipe;

        internal TlsClientPipeline(IPipe inputPipe, SSL_CTX context, ClientOptions clientOptions)
        {
            _connectionPipe = inputPipe;
            _innerPipe = new Pipe(new PipeOptions(System.Buffers.MemoryPool.Default));
            _context = context;
            _ssl = SSL_new(_context);
            _clientOptions = clientOptions;
        }

        internal async Task Authenticate()
        {
            while (true)
            {
                var result = SSL_do_handshake(_ssl);
                if(result < 0 )
                {
                    throw new NotImplementedException();
                }
                throw new NotImplementedException();
            }
        }

        public IPipeReader Reader => _innerPipe.Reader;

        public IPipeWriter Writer => _innerPipe.Writer;

        public void Dispose()
        {
            _context.Close();
            _ssl.Close();
        }
    }
}
