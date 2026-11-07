using System.Windows;
using System.Windows.Media;

namespace Beltpack.Overlay;

/// <summary>
/// A stub of the rubber whip antenna, drawn standing on the jack and leaning toward the viewer: the
/// knurled ferrule that screws on, the smooth metal collar above it, and the black rubber up to its
/// first joint, with the start of the next section, whose end faces the viewer. Lengths are in
/// antenna radii, from photos of the real antenna.
/// </summary>
internal static class Antenna
{
    // Drawn a little slimmer than the real one, so it doesn't crowd the bracket.
    private const double Slim = 0.8;
    // Degrees between the antenna and the line of sight: 0 points straight at the viewer. The rings
    // look rounder and lengths along the antenna shorter as it comes down.
    private const double ViewAngle = 24;
    private static readonly double Squash = Math.Cos(ViewAngle * Math.PI / 180);
    private static readonly double Foreshorten = Math.Sin(ViewAngle * Math.PI / 180);

    // Each part from the bottom: width and length in antenna radii.
    private const double FerruleWidth = 1.92, FerruleLength = 0.7;
    private const double CollarWidth = 1.96, CollarLength = 1.28;
    private const double RubberWidth = 2.0, RubberTopWidth = 1.92, RubberLength = 2.9;
    private const double TipWidth = 1.84, TipLength = 0.4;

    private static readonly Brush Ferrule = Freeze(Across(0x10, 0x30, 0x48, 0x22, 0x0A));
    private static readonly Brush Collar = Freeze(Across(0x1C, 0x50, 0x74, 0x38, 0x14));
    private static readonly Brush Rubber = Freeze(Across(0x06, 0x26, 0x3A, 0x16, 0x04));
    private static readonly Brush End = Freeze(new RadialGradientBrush(Color.FromRgb(0x34, 0x36, 0x38), Color.FromRgb(0x0C, 0x0C, 0x0D))
    {
        GradientOrigin = new Point(0.4, 0.35),
        Center = new Point(0.45, 0.45),
    });
    private static readonly Brush Shadow = Freeze(new RadialGradientBrush(Color.FromArgb(0xA0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)));
    private static readonly Pen Edge = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), 0.7));
    private static readonly Pen Knurl = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x90, 0x90, 0x90)), 0.5));

    private static double Length => (FerruleLength + CollarLength + RubberLength + TipLength) * Foreshorten;

    /// <summary>The area it covers, standing on a jack at <paramref name="jack"/> for an antenna of radius <paramref name="radius"/>.</summary>
    public static Rect Bounds(Point jack, double radius)
    {
        double r = radius * Slim;
        double top = jack.Y - r * (Length + TipWidth / 2 * Squash);
        return new Rect(jack.X - r, top, 2 * r, jack.Y + r * Squash - top);
    }

    public static void Draw(DrawingContext dc, Point jack, double radius)
    {
        double r = radius * Slim, cx = jack.X, y = jack.Y;
        dc.DrawEllipse(Shadow, null, new Point(cx, y + r * 0.3), r * 1.4, r * 0.9);

        double bottom = y, top = y - r * FerruleLength * Foreshorten;
        double half = r * FerruleWidth / 2;
        dc.DrawGeometry(Ferrule, Edge, Part(cx, bottom, top, half, half));
        for (int i = 1; i < 16; i++)
        {
            double t = 2.0 * i / 16 - 1, x = cx + t * half;
            dc.DrawLine(Knurl, new Point(x, top + Arc(t, half)), new Point(x, bottom + Arc(t, half)));
        }

        bottom = top;
        top -= r * CollarLength * Foreshorten;
        dc.DrawGeometry(Collar, Edge, Part(cx, bottom, top, r * CollarWidth / 2, r * CollarWidth / 2));

        bottom = top;
        top -= r * RubberLength * Foreshorten;
        dc.DrawGeometry(Rubber, Edge, Part(cx, bottom, top, r * RubberWidth / 2, r * RubberTopWidth / 2));

        bottom = top;
        top -= r * TipLength * Foreshorten;
        half = r * TipWidth / 2;
        dc.DrawGeometry(Rubber, Edge, Part(cx, bottom, top, half, half));
        dc.DrawEllipse(End, Edge, new Point(cx, top), half, half * Squash);
    }

    /// <summary>A length of cylinder from <paramref name="bottom"/> up to <paramref name="top"/>, its lower ring curving round toward the viewer.</summary>
    private static Geometry Part(double cx, double bottom, double top, double bottomHalf, double topHalf)
    {
        var part = new StreamGeometry();
        using (var g = part.Open())
        {
            g.BeginFigure(new Point(cx - topHalf, top), true, true);
            g.LineTo(new Point(cx - bottomHalf, bottom), true, false);
            g.ArcTo(new Point(cx + bottomHalf, bottom), new Size(bottomHalf, bottomHalf * Squash), 0, false, SweepDirection.Counterclockwise, true, false);
            g.LineTo(new Point(cx + topHalf, top), true, false);
        }
        part.Freeze();
        return part;
    }

    /// <summary>How far below its ends a ring curves, at <paramref name="t"/> from -1 (left) to 1 (right).</summary>
    private static double Arc(double t, double half) => Math.Sqrt(Math.Max(0, 1 - t * t)) * half * Squash;

    /// <summary>Shaded left to right like a lit cylinder, from five grey levels.</summary>
    private static LinearGradientBrush Across(byte edge, byte lit, byte highlight, byte shade, byte far) =>
        new(new GradientStopCollection
        {
            new(Grey(edge), 0),
            new(Grey(lit), 0.3),
            new(Grey(highlight), 0.42),
            new(Grey(shade), 0.65),
            new(Grey(far), 1),
        }, new Point(0, 0.5), new Point(1, 0.5));

    private static Color Grey(byte v) => Color.FromRgb(v, v, v);

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
