using System.Text.RegularExpressions;

namespace Beltpack.Core.Run8;

/// <summary>
/// Follows Run8.log for the unit number of the locomotive the player controls. Run8 doesn't send it
/// to custom input devices, but logs "Controlling-Unit is BNSF 8081" when the player takes a train.
/// </summary>
public sealed partial class Run8LogReader
{
    /// <summary>The controlling unit's number (e.g. "8081"), or null when the player has no train.</summary>
    public string? ControllingUnit { get; private set; }

    [GeneratedRegex(@"Controlling-Unit is .*?(\d+)\s*$")]
    private static partial Regex TakenPattern();

    [GeneratedRegex(@"Train Removed From World:.*?\D(\d+)\s*$")]
    private static partial Regex RemovedPattern();

    public void ReadLine(string line)
    {
        if (TakenPattern().Match(line) is { Success: true } taken)
            ControllingUnit = taken.Groups[1].Value;
        else if (RemovedPattern().Match(line) is { Success: true } removed && removed.Groups[1].Value == ControllingUnit)
            ControllingUnit = null;
        // Run8 starting up, or leaving the sim for the menus.
        else if (line.Contains("===== V3 ") || line.Contains("SimScreen Unload Content Complete"))
            ControllingUnit = null;
    }

    public void Reset() => ControllingUnit = null;
}
