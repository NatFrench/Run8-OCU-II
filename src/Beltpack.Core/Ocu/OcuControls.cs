namespace Beltpack.Core.Ocu;

// Positions are ordered from released/lowest to highest, matching the faceplate legends.

public enum IndBrake { Rel, Low, Med, Full, Emerg }

public enum AutoBrake { Rel, Min, Light, Med, Full, Charge }

public enum Direction { Rev, Neutral, Fwd }

public enum SpeedSelect { Stop, CoastB, Coast, Couple, Four, Seven, Ten, Max }

public enum BellHorn { Off, Bell, BellHorn }

/// <summary>
/// Snapshot of every physical control on the OCU. Momentary buttons are true while held.
/// </summary>
public sealed record OcuInput
{
    public bool PowerOn { get; init; }
    public IndBrake Ind { get; init; } = IndBrake.Rel;
    public AutoBrake Auto { get; init; } = AutoBrake.Rel;
    public Direction Dir { get; init; } = Direction.Neutral;
    public SpeedSelect Speed { get; init; } = SpeedSelect.Stop;
    public BellHorn BellHorn { get; init; } = BellHorn.Off;
    public bool Rsc { get; init; }
    public bool Pitch { get; init; }
    public bool Status { get; init; }
    public bool TiltTimeout { get; init; }
    public bool Sand { get; init; }
    public bool HeadlightBright { get; init; }
    public bool Tilted { get; init; }

    public static string Label(SpeedSelect s) => s switch
    {
        SpeedSelect.Stop => "Stop",
        SpeedSelect.CoastB => "Coast B",
        SpeedSelect.Coast => "Coast",
        SpeedSelect.Couple => "Couple",
        SpeedSelect.Four => "4",
        SpeedSelect.Seven => "7",
        SpeedSelect.Ten => "10",
        SpeedSelect.Max => "Max",
        _ => s.ToString(),
    };
}
