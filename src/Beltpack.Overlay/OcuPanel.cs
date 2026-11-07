using Beltpack.Core.Ocu;

namespace Beltpack.Overlay;

/// <summary>The panel dimmer's three settings for the lamps, icons and display.</summary>
public enum PanelBrightness { Low, Medium, Full }

/// <summary>
/// The physical state of the virtual OCU, changed by mouse and keyboard and read once per tick.
/// </summary>
public sealed class OcuPanel
{
    private BellHorn _bellBeforeHorn = BellHorn.Off;
    private bool _powerOn;

    /// <summary>
    /// The OCU always powers up with the train brake at Full, whatever AUTO was left at, and with the
    /// panel at full brightness.
    /// </summary>
    public bool PowerOn
    {
        get => _powerOn;
        set
        {
            if (value && !_powerOn)
            {
                Auto = AutoBrake.Full;
                Brightness = PanelBrightness.Full;
            }
            _powerOn = value;
        }
    }
    public PanelBrightness Brightness { get; set; } = PanelBrightness.Full;
    public IndBrake Ind { get; set; } = IndBrake.Rel;
    public AutoBrake Auto { get; set; } = AutoBrake.Rel;
    public Direction Dir { get; set; } = Direction.Neutral;
    public SpeedSelect Speed { get; set; } = SpeedSelect.Stop;
    public BellHorn BellHorn { get; set; } = BellHorn.Off;
    public bool Rsc { get; set; }
    public bool Pitch { get; set; }
    public bool Status { get; set; }
    public bool TiltTimeout { get; set; }
    public bool Sand { get; set; }
    public bool HeadlightBright { get; set; }
    public bool Tilted { get; set; }

    public event Action? Changed;

    public void Step<T>(Func<T> get, Action<T> set, int delta) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        int index = Array.IndexOf(values, get());
        int next = Math.Clamp(index + delta, 0, values.Length - 1);
        if (next != index)
        {
            set(values[next]);
            Changed?.Invoke();
        }
    }

    public void StepSpeed(int delta) => Step(() => Speed, v => Speed = v, delta);
    public void StepDir(int delta) => Step(() => Dir, v => Dir = v, delta);
    public void StepAuto(int delta) => Step(() => Auto, v => Auto = v, delta);
    public void StepInd(int delta) => Step(() => Ind, v => Ind = v, delta);

    /// <summary>Each press of the sun key steps the panel down a setting, from Low back round to Full.</summary>
    public void StepBrightness()
    {
        Brightness = Brightness == PanelBrightness.Low ? PanelBrightness.Full : Brightness - 1;
        Changed?.Invoke();
    }

    public void ToggleBell()
    {
        BellHorn = BellHorn == BellHorn.Off ? BellHorn.Bell : BellHorn.Off;
        Changed?.Invoke();
    }

    /// <summary>Moves the bat between its latching positions, Off and Bell; Horn is only ever held.</summary>
    public void StepBell(int delta)
    {
        if (BellHorn == BellHorn.BellHorn)
            return;
        BellHorn = delta > 0 ? BellHorn.Bell : BellHorn.Off;
        Changed?.Invoke();
    }

    /// <summary>Bell/Horn is momentary; releasing returns the toggle to where it was.</summary>
    public void SetHorn(bool held)
    {
        if (held && BellHorn != BellHorn.BellHorn)
        {
            _bellBeforeHorn = BellHorn;
            BellHorn = BellHorn.BellHorn;
        }
        else if (!held && BellHorn == BellHorn.BellHorn)
        {
            BellHorn = _bellBeforeHorn;
        }
        Changed?.Invoke();
    }

    public void Notify() => Changed?.Invoke();

    private readonly HashSet<string> _latched = new();

    /// <summary>Holds or releases a momentary button; a press is latched until the next snapshot.</summary>
    public void SetButton(string name, bool held)
    {
        switch (name)
        {
            case nameof(Rsc): Rsc = held; break;
            case nameof(Pitch): Pitch = held; break;
            case nameof(Status): Status = held; break;
            case nameof(TiltTimeout): TiltTimeout = held; break;
            case nameof(Sand): Sand = held; break;
            default: return;
        }
        if (held)
            _latched.Add(name);
        Changed?.Invoke();
    }

    public bool IsHeld(string name) => name switch
    {
        nameof(Rsc) => Rsc,
        nameof(Pitch) => Pitch,
        nameof(Status) => Status,
        nameof(TiltTimeout) => TiltTimeout,
        nameof(Sand) => Sand,
        _ => false,
    };

    public OcuInput Snapshot()
    {
        var input = new OcuInput
        {
            PowerOn = PowerOn,
            Ind = Ind,
            Auto = Auto,
            Dir = Dir,
            Speed = Speed,
            BellHorn = BellHorn,
            Rsc = Rsc || _latched.Contains(nameof(Rsc)),
            Pitch = Pitch || _latched.Contains(nameof(Pitch)),
            Status = Status || _latched.Contains(nameof(Status)),
            TiltTimeout = TiltTimeout || _latched.Contains(nameof(TiltTimeout)),
            Sand = Sand || _latched.Contains(nameof(Sand)),
            HeadlightBright = HeadlightBright,
            Tilted = Tilted,
        };
        _latched.Clear();
        return input;
    }
}
