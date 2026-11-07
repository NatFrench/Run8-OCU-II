using Beltpack.Core.Rcl;
using Beltpack.Core.Run8;

namespace Beltpack.Core.Tests;

public class CommandSenderTests
{
    [Fact]
    public void SendsOnlyChangesBetweenRefreshes()
    {
        var sent = new List<(Run8Message Type, byte Value)>();
        var sender = new CommandSender((t, v) => sent.Add((t, v)), refreshSeconds: 1.0);

        sender.Send(new LocoCommand(), 0.1);
        sent.Clear();
        sender.Send(new LocoCommand { Throttle = 3, Horn = true }, 0.1);

        Assert.Equal(new[] { (Run8Message.ThrottleLever, (byte)3), (Run8Message.Horn, (byte)1) }, sent);
    }

    [Fact]
    public void RefreshResendsLeversButNotButtons()
    {
        var sent = new List<(Run8Message Type, byte Value)>();
        var sender = new CommandSender((t, v) => sent.Add((t, v)), refreshSeconds: 0.5);
        var command = new LocoCommand { Throttle = 2, Reverser = ReverserState.Fwd };

        sender.Send(command, 0.1);
        sent.Clear();
        sender.Send(command, 0.5);

        Assert.Contains((Run8Message.ThrottleLever, (byte)2), sent);
        Assert.Contains((Run8Message.ReverserLever, Run8Protocol.ReverserForward), sent);
        Assert.DoesNotContain(sent, s => s.Type == Run8Message.Horn);
    }

    [Fact]
    public void SlowSpeedControlIsSwitchedOffOncePerChangeover()
    {
        var sent = new List<(Run8Message Type, byte Value)>();
        var sender = new CommandSender((t, v) => sent.Add((t, v)), refreshSeconds: 0.5);

        for (int i = 0; i < 10; i++)
            sender.Send(new LocoCommand(), 0.5);
        Assert.Single(sent, s => s == (Run8Message.SlowSpeedOnOff, 0));

        sender.Reset();
        sender.Send(new LocoCommand(), 0.1);
        Assert.Equal(2, sent.Count(s => s == (Run8Message.SlowSpeedOnOff, 0)));
    }

    /// <summary>Run8's bell button: each press turns the bell on or off.</summary>
    private sealed class ToggleBell
    {
        public bool On;
        private bool _pressed;

        public void Receive(Run8Message type, byte value)
        {
            if (type != Run8Message.Bell)
                return;
            if (value == 1 && !_pressed)
                On = !On;
            _pressed = value == 1;
        }

        public Run8Telemetry Telemetry => new() { LocoStatus1 = On ? LocoStatus1.BellOn : 0 };
    }

    private static void Run(CommandSender sender, ToggleBell bell, bool wanted, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.05)
            sender.Send(new LocoCommand { Bell = wanted }, 0.05, bell.Telemetry);
    }

    [Fact]
    public void BellSwitchTurnsRun8sBellOnAndOff()
    {
        var bell = new ToggleBell();
        var sender = new CommandSender(bell.Receive);

        Run(sender, bell, false, 1);
        Assert.False(bell.On);
        Run(sender, bell, true, 2);
        Assert.True(bell.On);
        Run(sender, bell, false, 2);
        Assert.False(bell.On);
        Run(sender, bell, true, 2);
        Assert.True(bell.On);
    }

    [Fact]
    public void BellIsLeftAloneWhenTheSwitchHasntMoved()
    {
        var bell = new ToggleBell();
        var sender = new CommandSender(bell.Receive);
        Run(sender, bell, false, 5);

        bell.On = true;
        Run(sender, bell, false, 5);

        Assert.True(bell.On);
    }
}
