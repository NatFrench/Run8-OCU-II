using Beltpack.Core.Run8;

namespace Beltpack.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public void HornPressedMatchesSpecExample()
    {
        Assert.Equal(new byte[] { 96, 0, 8, 1, 105 }, Run8Protocol.BuildInput(Run8Message.Horn, 1));
    }

    [Fact]
    public void AudioFlagSetsHighBitOfHeader()
    {
        Assert.Equal(224, Run8Protocol.BuildInput(Run8Message.Bell, 1, playAudio: true)[0]);
    }

    [Theory]
    [InlineData(129, 0.1)]
    [InlineData(227, 9.9)]
    [InlineData(12, 12.0)]
    [InlineData(127, 127.0)]
    public void DecodesSpeed(byte raw, double mph)
    {
        Assert.Equal(mph, Run8Protocol.DecodeSpeed(raw), 3);
    }

    [Theory]
    [InlineData(107, -16.1)]
    [InlineData(139, 9.0)]
    public void DecodesAcceleration(byte raw, double mphPerMin)
    {
        Assert.Equal(mphPerMin, Run8Protocol.DecodeAcceleration(raw), 0);
    }

    [Fact]
    public void ParsesTelemetryPacket()
    {
        var packet = TelemetryPacket(speed: 128 + 45, mr: 140, er: 90, bp: 89, feed: 90, status1: (byte)LocoStatus1.WheelSlip);

        Assert.True(Run8Protocol.TryParseTelemetry(packet, out var t));
        Assert.Equal(4.5, t.SpeedMph, 3);
        Assert.Equal(140, t.MainReservoirPsi);
        Assert.Equal(89, t.BrakePipePsi);
        Assert.Equal(90, t.FeedValvePsi);
        Assert.True(t.LocoStatus1.HasFlag(LocoStatus1.WheelSlip));
    }

    [Fact]
    public void RejectsBadCrc()
    {
        var packet = TelemetryPacket(speed: 5, mr: 140, er: 90, bp: 90, feed: 90, status1: 0);
        packet[^1] ^= 0xFF;

        Assert.False(Run8Protocol.TryParseTelemetry(packet, out _));
    }

    internal static byte[] TelemetryPacket(byte speed, byte mr, byte er, byte bp, byte feed, byte status1)
    {
        var p = new byte[27];
        p[0] = Run8Protocol.OutputHeader;
        p[1] = speed;
        p[2] = mr;
        p[3] = er;
        p[4] = bp;
        p[10] = 255;
        p[15] = status1;
        p[19] = 128;
        p[20] = 128;
        p[25] = feed;
        p[26] = Run8Protocol.Crc(p, 26);
        return p;
    }
}
