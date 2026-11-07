using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Beltpack.Overlay;

/// <summary>
/// The photo skin made by tools/make_skin.py: bitmap layers cut from photos of a real OCU, and
/// where its lamps, buttons and display sit. All positions are in overlay units.
/// </summary>
public sealed class SkinLayout
{
    public double Width { get; set; }
    public double Height { get; set; }
    public SkinLayer Base { get; set; } = new();
    /// <summary>Each paddle, toggle and knob, photographed in each of its positions.</summary>
    public Dictionary<string, SkinControl> Controls { get; set; } = new();
    public Dictionary<string, SkinButton> Buttons { get; set; } = new();
    public Dictionary<string, SkinLampGroup> Lamps { get; set; } = new();
    public double LampRadius { get; set; }
    public SkinRect Display { get; set; } = new();
    public Dictionary<string, SkinIcon> Icons { get; set; } = new();
    /// <summary>The antenna jack on the bracket, and the radius of the antenna fitted to it.</summary>
    public SkinIcon Antenna { get; set; } = new();
    public Dictionary<string, SkinRect> Legends { get; set; } = new();
    /// <summary>The printed word beside each lamp, in lamp order (tools/make_legend_text.py).</summary>
    public Dictionary<string, SkinRect[]> LegendText { get; set; } = new();
    /// <summary>Base-sized and white, opaque only on the words in <see cref="LegendText"/>.</summary>
    public BitmapSource? LegendTextMask { get; private set; }

    public static SkinLayout Load()
    {
        using var stream = Open("skin.json");
        var layout = JsonSerializer.Deserialize<SkinLayout>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("skin.json is empty");
        layout.Base.LoadImage();
        foreach (var pose in layout.Controls.Values.SelectMany(c => c.Poses.Values))
            pose.LoadImage();
        if (layout.LegendText.Count > 0)
            layout.LegendTextMask = SkinLayer.LoadBitmap("legend_text.png");
        return layout;
    }

    internal static Stream Open(string file) =>
        Application.GetResourceStream(new Uri($"pack://application:,,,/Skin/{file}"))?.Stream
        ?? throw new FileNotFoundException($"Skin resource {file} is missing");
}

public sealed class SkinRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    public Rect ToRect() => new(X, Y, W, H);
}

/// <summary>
/// A control's photo in each position, keyed "centre", "away" and "toward" (from the operator) for
/// the paddles and toggles, and by detent number from the first for the knobs.
/// </summary>
public sealed class SkinControl
{
    /// <summary>Everything the control covers in any position.</summary>
    public SkinRect Hit { get; set; } = new();
    public Dictionary<string, SkinLayer> Poses { get; set; } = new();
}

public sealed class SkinLayer
{
    public string File { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    public Rect Bounds => new(X, Y, W, H);
    public ImageSource? Image { get; private set; }

    internal void LoadImage()
    {
        if (File.Length > 0)
            Image = LoadBitmap(File);
    }

    internal static BitmapSource LoadBitmap(string file)
    {
        using var stream = SkinLayout.Open(file);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

public sealed class SkinButton
{
    public double[] Center { get; set; } = { 0, 0 };
    public double Dome { get; set; }
    /// <summary>The dome's radius down the face, where the photo sees it at a slant; 0 if round.</summary>
    public double DomeY { get; set; }
    public double Bezel { get; set; }

    public Point CenterPoint => new(Center[0], Center[1]);
}

public sealed class SkinLampGroup
{
    public double[][] Points { get; set; } = Array.Empty<double[]>();
    public double[] Legend { get; set; } = { 0, 0 };
}

public sealed class SkinIcon
{
    public double[] Center { get; set; } = { 0, 0 };
    public double Radius { get; set; }

    public Point CenterPoint => new(Center[0], Center[1]);
}
