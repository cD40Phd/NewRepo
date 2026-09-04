using EnergyBalance.Acquisition.Protocols.Mercury;
using Xunit;

namespace EnergyBalance.Tests;

public class MercuryCrcTests
{
    [Fact]
    public void AppendCrc_ThenValidate_ReturnsTrue()
    {
        byte[] frame = [0x01, 0x08, 0x11];

        var framed = MercuryCrc.AppendCrc(frame);

        Assert.Equal(frame.Length + 2, framed.Length);
        Assert.True(MercuryCrc.Validate(framed));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Validate_DetectsSingleByteCorruption(int byteIndexToCorrupt)
    {
        byte[] frame = [0x05, 0x02, 0xAA, 0x7F];
        var framed = MercuryCrc.AppendCrc(frame);

        framed[byteIndexToCorrupt] ^= 0xFF;

        Assert.False(MercuryCrc.Validate(framed));
    }

    [Fact]
    public void Validate_ReturnsFalse_ForTooShortFrame()
    {
        Assert.False(MercuryCrc.Validate(new byte[] { 0x01, 0x02 }));
    }

    [Fact]
    public void Compute_IsDeterministic()
    {
        byte[] data = [0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01];

        var crc1 = MercuryCrc.Compute(data);
        var crc2 = MercuryCrc.Compute(data);

        Assert.Equal(crc1, crc2);
    }
}
