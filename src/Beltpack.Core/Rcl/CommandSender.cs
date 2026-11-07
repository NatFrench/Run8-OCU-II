using Beltpack.Core.Run8;

namespace Beltpack.Core.Rcl;

/// <summary>
/// Pushes a <see cref="LocoCommand"/> to Run8: changed values immediately, and the levers again on
/// a refresh interval so keyboard or mouse input in the sim can't leave them out of step.
/// </summary>
public sealed class CommandSender
{
    // Run8's bell is a push button that turns the bell on or off with each press, so the bell switch
    // is matched to Run8's reported bell state by pressing it, for a while after each switch move.
    public const double BellPressSeconds = 0.15;
    public const double BellConfirmSeconds = 1.0;
    public const double BellMatchSeconds = 3.0;

    private readonly Action<Run8Message, byte> _send;
    private readonly double _refreshSeconds;
    private readonly Dictionary<Run8Message, byte> _last = new();
    private double _sinceRefresh;
    private bool? _bellWanted;
    private double _bellPressLeft, _bellConfirmLeft, _bellMatchLeft;

    public CommandSender(Action<Run8Message, byte> send, double refreshSeconds = 0.5)
    {
        _send = send;
        _refreshSeconds = refreshSeconds;
    }

    public void Reset()
    {
        _last.Clear();
        _bellWanted = null;
        if (_bellPressLeft > 0)
        {
            _bellPressLeft = 0;
            _send(Run8Message.Bell, 0);
        }
    }

    public void Send(LocoCommand c, double dt, Run8Telemetry? telemetry = null)
    {
        SendBell(c.Bell, dt, telemetry);
        _sinceRefresh += dt;
        bool refresh = _sinceRefresh >= _refreshSeconds;
        if (refresh)
            _sinceRefresh = 0;

        Put(Run8Message.ThrottleLever, (byte)c.Throttle, refresh);
        Put(Run8Message.IndyBrakeLever, c.Indy, refresh);
        Put(Run8Message.TrainBrakeLever, c.TrainBrake, refresh);
        Put(Run8Message.ReverserLever, c.Reverser switch
        {
            ReverserState.Fwd => Run8Protocol.ReverserForward,
            ReverserState.Rev => Run8Protocol.ReverserReverse,
            _ => Run8Protocol.ReverserNeutral,
        }, refresh);
        Put(Run8Message.DynBrakeLever, 0, refresh);
        Put(Run8Message.HeadlightFront, c.HeadlightFront, false);
        Put(Run8Message.HeadlightRear, c.HeadlightRear, false);
        Put(Run8Message.Horn, Bool(c.Horn), false);
        Put(Run8Message.Sander, Bool(c.Sander), false);
        Put(Run8Message.Alerter, Bool(c.Alerter), false);
        // Run8's slow speed control cuts power at whatever speed it was set at, which would cap the
        // selected speed. Its state isn't in telemetry, so it's switched off once per changeover.
        Put(Run8Message.SlowSpeedOnOff, 0, false);
    }

    private void SendBell(bool wanted, double dt, Run8Telemetry? telemetry)
    {
        bool moved = _bellWanted is not null && _bellWanted != wanted;
        if (_bellWanted != wanted)
        {
            _bellWanted = wanted;
            _bellMatchLeft = BellMatchSeconds;
            _bellConfirmLeft = 0;
        }

        if (_bellPressLeft > 0)
        {
            _bellPressLeft -= dt;
            if (_bellPressLeft <= 0)
            {
                _send(Run8Message.Bell, 0);
                _bellConfirmLeft = BellConfirmSeconds;
            }
            return;
        }

        _bellConfirmLeft -= dt;
        _bellMatchLeft -= dt;
        bool press = telemetry is null
            ? moved
            : _bellMatchLeft > 0 && _bellConfirmLeft <= 0
              && telemetry.LocoStatus1.HasFlag(LocoStatus1.BellOn) != wanted;
        if (press)
        {
            _send(Run8Message.Bell, 1);
            _bellPressLeft = BellPressSeconds;
        }
    }

    private void Put(Run8Message type, byte value, bool force)
    {
        if (!force && _last.TryGetValue(type, out var previous) && previous == value)
            return;
        _last[type] = value;
        _send(type, value);
    }

    private static byte Bool(bool b) => b ? (byte)1 : (byte)0;
}
