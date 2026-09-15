using System;
using System.IO;
using System.IO.Compression;

namespace TankDraft.Infrastructure.FusionTransport
{
    // Versioned response framing only; compression provides no confidentiality or authentication.
    public static class FusionQaWireCodec
    {
        public const int MaximumDecodedBytes = 1048704;
        public const int MaximumWireBytes = MaximumDecodedBytes + 9;
        public static byte[] Encode(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > MaximumDecodedBytes) throw new InvalidDataException();
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var compressor = new DeflateStream(output, CompressionLevel.Fastest, true)) compressor.Write(payload, 0, payload.Length);
                compressed = output.ToArray();
            }
            bool useCompression = compressed.Length < payload.Length;
            var body = useCompression ? compressed : payload;
            var wire = new byte[body.Length + 9];
            wire[0] = (byte)'T'; wire[1] = (byte)'D'; wire[2] = (byte)'Q'; wire[3] = 1;
            wire[4] = useCompression ? (byte)1 : (byte)0;
            for (int i = 0; i < 4; i++) wire[5 + i] = (byte)(payload.Length >> (8 * i));
            Buffer.BlockCopy(body, 0, wire, 9, body.Length);
            return wire;
        }
        public static byte[] Decode(byte[] wire)
        {
            if (wire == null || wire.Length < 10 || wire.Length > MaximumWireBytes ||
                wire[0] != 'T' || wire[1] != 'D' || wire[2] != 'Q' || wire[3] != 1 || wire[4] > 1) throw new InvalidDataException();
            int length = wire[5] | wire[6] << 8 | wire[7] << 16 | wire[8] << 24;
            if (length <= 0 || length > MaximumDecodedBytes) throw new InvalidDataException();
            var payload = new byte[length];
            if (wire[4] == 0)
            {
                if (wire.Length != length + 9) throw new InvalidDataException();
                Buffer.BlockCopy(wire, 9, payload, 0, length);
                return payload;
            }
            using (var input = new MemoryStream(wire, 9, wire.Length - 9, false))
            using (var decoder = new DeflateStream(input, CompressionMode.Decompress))
            {
                int offset = 0;
                while (offset < length)
                {
                    int count = decoder.Read(payload, offset, length - offset);
                    if (count == 0) throw new InvalidDataException();
                    offset += count;
                }
                if (decoder.ReadByte() != -1) throw new InvalidDataException();
            }
            return payload;
        }
    }
}
