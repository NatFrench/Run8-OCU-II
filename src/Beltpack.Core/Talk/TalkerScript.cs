using System.Text.RegularExpressions;

namespace Beltpack.Core.Talk;

/// <summary>One recorded clip to play, named by its path in the talker folder without the extension.</summary>
public readonly record struct TalkerClip(string Name, bool EndsSentence);

/// <summary>
/// Turns the talker's text into the recorded clips that say it. Sentences, or parts of them, with no
/// recording are left out and listed as missing.
/// </summary>
public static partial class TalkerScript
{
    private static readonly Dictionary<string, string> Phrases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Locomotive in remote mode"] = "Phrases/LocoInRemoteMode",
        ["Brake recovery complete"] = "Phrases/BrakeRecoveryComplete",
        ["Brake pipe fully charged"] = "Phrases/BPFullyCharged",
        ["Severe wheel slip"] = "Phrases/SevereWheelslip",
        ["Locomotive movement failure"] = "Phrases/LocoMoveFail",
        ["Communication interrupted"] = "Phrases/CommInterrupted",
        ["Operator requested emergency"] = "Phrases/OpRequestedEMG",
        ["Operator requested train brake"] = "Phrases/OpRequestedTrainBrake",
        ["Illegal reverser change"] = "Phrases/IllegalReverserChange",
        ["Low brake pipe pressure"] = "Phrases/LowBrakePipePressure",
        ["Recover emergency from beltpack"] = "Phrases/RecoverEMGfromBelt",
        ["Recover service from beltpack"] = "Phrases/RecoverServiceBeltpack",
        ["Recover train brake from beltpack"] = "Phrases/RecoverTBfromBelt",
        ["Operator A down"] = "Phrases/OpADown",
        ["Operator B down"] = "Phrases/OpBDown",
        ["Consist ready"] = "Phrases/ConsistReady",
        ["Locomotive alarm"] = "Phrases/LocomotiveAlarm",
        ["System fault"] = "Phrases/SystemFault",
        ["Zero speed encountered"] = "Phrases/ZeroSpeedEncountered",
        ["Pullback protection on"] = "Phrases/PullbackProtectOn",
        ["Pullback protection off"] = "Phrases/PullbackProtectOff",
        ["Pullback protection override"] = "Phrases/PullbackProtectOverride",
        ["On the move forward"] = "Phrases/OTM Forward",
        ["On the move reverse"] = "Phrases/OTM Reverse",
        ["Parking brake command in progress"] = "Phrases/ParkBrkCom",
        ["Parking brake release"] = "Phrases/ParkBrkRelease",
        ["Remote hump pitch successful"] = "Phrases/RHP Successful",
        ["Out"] = "Phrases/Out",
        // Bare OCU letters (e.g. after RecoverEMG when putting the engine in remote).
        ["A"] = "Phrases/A",
        ["B"] = "Phrases/B",
        ["AnB"] = "Phrases/AnB",
    };

    /// <summary>The railroads with a recorded name.</summary>
    public static IReadOnlyList<string> RecordedRailroads { get; } = ["BNSF", "CSX", "NS", "UP", "RER"];

    private static readonly HashSet<string> Railroads = new(RecordedRailroads, StringComparer.OrdinalIgnoreCase);

    public static (List<TalkerClip> Clips, List<string> Missing) Parse(string text)
    {
        var clips = new List<TalkerClip>();
        var missing = new List<string>();
        foreach (var raw in text.Split(". ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sentence = new List<string>();
            Sentence(raw.TrimEnd('.'), sentence, missing);
            for (int i = 0; i < sentence.Count; i++)
                clips.Add(new TalkerClip(sentence[i], i == sentence.Count - 1));
        }
        return (clips, missing);
    }

    private static void Sentence(string s, List<string> clips, List<string> missing)
    {
        if (Phrases.TryGetValue(s, out var phrase))
        {
            clips.Add(phrase);
            return;
        }

        if (s.StartsWith("Cause: ", StringComparison.OrdinalIgnoreCase))
        {
            missing.Add("Cause");
            Sentence(s["Cause: ".Length..], clips, missing);
            return;
        }

        var tilt = TiltTimeout().Match(s);
        if (tilt.Success)
        {
            clips.Add("Phrases/TiltTimeoutFrom");
            clips.Add(OcuClip(tilt.Groups["letter"].Value));
            return;
        }

        var unit = Unit().Match(s);
        if (unit.Success)
        {
            string name = unit.Groups["name"].Value.Replace(" ", "");
            if (Railroads.Contains(name))
                clips.Add($"Railroads/{name.ToUpperInvariant()}");
            else
                missing.Add(unit.Groups["name"].Value);
            foreach (char d in unit.Groups["digits"].Value.Where(char.IsDigit))
                clips.Add($"Numbers/{d}");
            missing.Add("O C U");
            clips.Add(OcuClip(unit.Groups["letter"].Value));
            return;
        }

        missing.Add(s);
    }

    private static string OcuClip(string letter) =>
        letter.Equals("AnB", StringComparison.OrdinalIgnoreCase) ? "Phrases/AnB"
        : $"Phrases/{char.ToUpperInvariant(letter[0])}";

    [GeneratedRegex(@"^Tilt timeout from (?<letter>AnB|[AB])$", RegexOptions.IgnoreCase)]
    private static partial Regex TiltTimeout();

    // The status report's opening, e.g. "B N S F 4 0 2 6, O C U A" or "Locomotive 4 0 2 6, O C U AnB".
    [GeneratedRegex(@"^(?<name>.+?) (?<digits>\d( \d)*), O C U (?<letter>AnB|[AB])$", RegexOptions.IgnoreCase)]
    private static partial Regex Unit();
}
