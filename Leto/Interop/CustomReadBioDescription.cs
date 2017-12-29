using System;
using System.IO.Pipelines;
using static Leto.Interop.LibCrypto;

namespace Leto.Interop
{
    internal class CustomReadBioDescription : CustomBioDescription
    {
        public CustomReadBioDescription()
            :base(nameof(CustomReadBioDescription))
        {
        }

        protected override int Create(BIO bio) => 1;

        protected override int Destroy(BIO bio) => 1;

        protected override int Read(BIO bio, Span<byte> output)
        {
            ref var data = ref BIO_get_data<ReadableBuffer>(bio);

            if(data.Length <= 0)
            {
                return -1;
            }

            var amountToWrite = Math.Min(data.Length, output.Length);
            data.Slice(0, amountToWrite).CopyTo(output);
            data = data.Slice(amountToWrite);

            return (int) amountToWrite;
        }

        protected override int Write(BIO bio, ReadOnlySpan<byte> input)
        {
            throw new NotImplementedException();
        }
    }
}
