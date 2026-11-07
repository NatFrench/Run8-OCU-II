using System.Net;
using System.Net.Sockets;
using Beltpack.Core.Run8;

namespace Beltpack.Core.Tests;

public class Run8LinkTests
{
    private const int Port = 28888;

    [Fact]
    public async Task SendsInputPacketsAndReceivesTelemetryOverLoopback()
    {
        using var sim = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
        using var link = new Run8Link(Port);

        link.Send(Run8Message.ThrottleLever, 5);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = await sim.ReceiveAsync(timeout.Token);
        Assert.Equal(Run8Protocol.BuildInput(Run8Message.ThrottleLever, 5), received.Buffer);

        var packet = ProtocolTests.TelemetryPacket(speed: 128 + 37, mr: 140, er: 85, bp: 84, feed: 90, status1: 0);
        await sim.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, Port + 1));

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (link.Latest is null && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.NotNull(link.Latest);
        Assert.Equal(3.7, link.Latest!.SpeedMph, 3);
        Assert.Equal(84, link.Latest.BrakePipePsi);
        Assert.True(link.TelemetryAge < TimeSpan.FromSeconds(1));
    }
}
