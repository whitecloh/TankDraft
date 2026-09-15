using System.IO.Compression;
using TankDraft.Infrastructure.FusionTransport;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed class FusionQaWireCodecTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8192)]
    [InlineData(FusionQaWireCodec.MaximumDecodedBytes)]
    public void RoundTripRepeatedPayload(int size)
    {
        var payload = Enumerable.Repeat((byte)65, size).ToArray();
        var wire = FusionQaWireCodec.Encode(payload);
        Assert.Equal(payload, FusionQaWireCodec.Decode(wire));
        if (size > 100) Assert.True(wire.Length < size / 10);
    }
    [Fact]
    public void IncompressiblePayloadUsesRawFrame()
    {
        var payload = new byte[64]; new Random(42).NextBytes(payload);
        var wire = FusionQaWireCodec.Encode(payload);
        Assert.Equal(0, wire[4]);
        Assert.Equal(payload, FusionQaWireCodec.Decode(wire));
        Assert.Throws<InvalidDataException>(() => FusionQaWireCodec.Decode(wire[..^1]));
    }
    [Fact]
    public void InvalidHeaderAndOversizedDeclarationAreRejected()
    {
        var wire = FusionQaWireCodec.Encode(new byte[1000]);
        foreach (int index in new[] { 0, 1, 2, 3, 4 })
        {
            var malformed = (byte[])wire.Clone(); malformed[index] = 255;
            Assert.Throws<InvalidDataException>(() => FusionQaWireCodec.Decode(malformed));
        }
        wire[8] = 127;
        Assert.Throws<InvalidDataException>(() => FusionQaWireCodec.Decode(wire));
        Assert.Throws<InvalidDataException>(() => FusionQaWireCodec.Encode(new byte[FusionQaWireCodec.MaximumDecodedBytes + 1]));
    }
    [Fact]
    public void ExpandedOutputCannotExceedDeclaredBound()
    {
        var wire = FusionQaWireCodec.Encode(new byte[100000]);
        wire[5] = 16; wire[6] = wire[7] = wire[8] = 0;
        Assert.Throws<InvalidDataException>(() => FusionQaWireCodec.Decode(wire));
    }
    [Fact]
    public void ShortDecompressedOutputIsRejected()
    {
        var wire = FusionQaWireCodec.Encode(new byte[1000]);
        wire[5] = 255; wire[6] = 255;
        Assert.Throws<InvalidDataException>(() => FusionQaWireCodec.Decode(wire));
        Assert.Throws<InvalidDataException>(() => FusionQaWireCodec.Decode(wire[..10]));
    }
}
