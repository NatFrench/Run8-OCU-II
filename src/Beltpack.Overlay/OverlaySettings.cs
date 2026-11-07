using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Beltpack.Core.Rcl;

namespace Beltpack.Overlay;

public sealed class OverlaySettings
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Run8RclController");

    public static string FilePath { get; } = Path.Combine(Folder, "settings.json");

    /// <summary>
    /// Raise when the RclSettings defaults change: older files then get the new tuning, keeping
    /// only the locomotive, OCU letter, channel and port.
    /// </summary>
    private const int CurrentVersion = 8;

    public int Version { get; set; }

    public RclSettings Rcl { get; set; } = new();

    /// <summary>Run8's install folder, for reading the controlling unit from Run8.log if it can't be found from the running game.</summary>
    public string Run8Folder { get; set; } = "";

    /// <summary>Use the locomotive the player controls in Run8 (from Run8.log) instead of Rcl.LocomotiveId.</summary>
    public bool LocomotiveFromRun8 { get; set; } = true;

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.95;
    /// <summary>Draw a stub of the whip antenna on the jack.</summary>
    public bool ShowAntenna { get; set; } = true;

    public bool TalkerEnabled { get; set; } = true;
    /// <summary>Percent: 25, 50, 75 or 100.</summary>
    public int TalkerVolume { get; set; } = 50;
    /// <summary>Say at startup when the talker recordings can't be found.</summary>
    public bool WarnIfTalkerMissing { get; set; } = true;
    public float SoundVolume { get; set; } = 0.35f;

    /// <summary>Stop Run8 from also seeing keys bound here while it has focus.</summary>
    public bool SwallowBoundKeys { get; set; } = true;

    /// <summary>Action name to WPF <see cref="System.Windows.Input.Key"/> name.</summary>
    public Dictionary<string, string> Keys { get; set; } = new()
    {
        [KeyAction.SpeedUp] = "NumPad8",
        [KeyAction.SpeedDown] = "NumPad2",
        [KeyAction.DirForward] = "NumPad6",
        [KeyAction.DirReverse] = "NumPad4",
        [KeyAction.AutoApply] = "NumPad9",
        [KeyAction.AutoRelease] = "NumPad3",
        [KeyAction.IndApply] = "NumPad7",
        [KeyAction.IndRelease] = "NumPad1",
        [KeyAction.Rsc] = "NumPad5",
        [KeyAction.Horn] = "NumPad0",
        [KeyAction.Bell] = "Decimal",
        [KeyAction.Status] = "Add",
        [KeyAction.TiltTimeout] = "Subtract",
        [KeyAction.Pitch] = "Divide",
        [KeyAction.Headlight] = "Multiply",
        [KeyAction.Sand] = "Scroll",
        // TILT DISABLED for the v1.2 play-test build.
        // [KeyAction.Tilt] = "Pause",
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static OverlaySettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string text = File.ReadAllText(FilePath);
                // TalkerVolume used to be on a louder 0-100 scale, then briefly a word; anything
                // other than the four steps goes back to the default.
                text = Regex.Replace(text, "\"TalkerVolume\":\\s*(?!(25|50|75|100)\\b)(\\d+|\"\\w*\"),?", "");
                var loaded = JsonSerializer.Deserialize<OverlaySettings>(text, Json) ?? new();
                if (loaded.Version < CurrentVersion)
                {
                    var old = loaded.Rcl;
                    loaded.Rcl = new RclSettings
                    {
                        Run8Port = old.Run8Port,
                        LocomotiveId = old.LocomotiveId,
                        Railroad = old.Railroad,
                        OcuLetter = old.OcuLetter,
                        Channel = old.Channel,
                    };
                    loaded.Version = CurrentVersion;
                    loaded.Save();
                }
                return loaded;
            }
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".bad", overwrite: true);
        }
        var fresh = new OverlaySettings { Version = CurrentVersion };
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}

public static class KeyAction
{
    public const string SpeedUp = "SpeedUp";
    public const string SpeedDown = "SpeedDown";
    public const string DirForward = "DirForward";
    public const string DirReverse = "DirReverse";
    public const string AutoApply = "AutoApply";
    public const string AutoRelease = "AutoRelease";
    public const string IndApply = "IndApply";
    public const string IndRelease = "IndRelease";
    public const string Rsc = "Rsc";
    public const string Horn = "Horn";
    public const string Bell = "Bell";
    public const string Status = "Status";
    public const string TiltTimeout = "TiltTimeout";
    public const string Pitch = "Pitch";
    public const string Headlight = "Headlight";
    public const string Brightness = "Brightness";
    public const string Sand = "Sand";
    public const string Tilt = "Tilt";
    public const string Power = "Power";
    public const string Changeover = "Changeover";
}
