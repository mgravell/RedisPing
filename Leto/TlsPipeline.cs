using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;

namespace Leto
{
    public static class TlsPipeline
    {
        public static async Task<TlsClientPipeline> AuthenticateClient(IPipeConnection inputPipe, ClientOptions clientOptions)
        {
            var ctx = Interop.OpenSsl.SSL_CTX_new(Interop.OpenSsl.TLS_client_method());
            var pipeline = new TlsClientPipeline(inputPipe, ctx, clientOptions);
            await pipeline.AuthenticateAsync();
            return pipeline;
        }
    }
}
