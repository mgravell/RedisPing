using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using static Leto.Interop.LibCrypto;

namespace Leto.Interop
{
    internal class CustomWriteBioDescription : CustomBioDescription
    {
        public CustomWriteBioDescription()
            : base(nameof(CustomWriteBioDescription))
        {
        }

        protected override int Create(BIO bio) => 1;

        protected override int Destroy(BIO bio) => 1;

        protected override int Read(BIO bio, Span<byte> output)
        {
            throw new NotImplementedException();
        }

        private const int MaxSize = 4096 - 64;

        protected override int Write(BIO bio, ReadOnlySpan<byte> input)
        {
            var data = BIO_get_data(bio);

            if(!(data.Target is IPipeWriter clientPipe))
            {
                return -1;
            }

            var inputLength = input.Length;

            var writer = clientPipe.Alloc();
            while (input.Length > 0)
            {
                var size = Math.Min(MaxSize, input.Length);
                writer.Ensure(size);
                input.Slice(0, size).CopyTo(writer.Buffer.Span);
                input = input.Slice(size);
                writer.Advance(size);
            }
            writer.Commit();
            return inputLength;
        }
    }
}
