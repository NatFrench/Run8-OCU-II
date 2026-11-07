using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Beltpack.Core.Ocu;
using Beltpack.Core.Rcl;

namespace Beltpack.Overlay;

/// <summary>
/// Development aid: "Beltpack.Overlay.exe --render-icons &lt;folder&gt;" draws the program icon at
/// each Windows icon size in several styles (the photo faceplate, the photo with a plain grey face,
/// a close-up of the middle of the unit, and a flat vector drawing), writes each as PNGs, writes
/// candidate .ico files mixing the photo at large sizes with a simpler style at small sizes, and a
/// comparison sheet.
/// </summary>
public static class IconRender
{
    private static readonly int[] Sizes = { 256, 128, 64, 48, 32, 24, 16 };

    // Sizes at or below this use the simplified styles in the mixed icons.
    private const int SmallSize = 48;

    // The grey face panel inside its bevel, from the face edges in tools/make_skin.py. Both sides lean
    // out toward the bottom, following the case as seen from above.
    private static readonly Point[] FaceOutline =
    {
        new(81, 107), new(88, 101), new(618, 101), new(625, 106), new(669, 270), new(663, 276),
        new(42, 273), new(36, 267),
    };

    // The middle of the unit: both paddles, both toggles and the antenna bracket over the face.
    private static readonly Rect CloseUp = new(172, 0, 400, 310);

    public static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        var skin = SkinLayout.Load();
        var photo = PhotoFaceplate(skin);
        var whole = new Rect(0, 0, skin.Width, skin.Height);
        void Photo(DrawingContext dc) => dc.DrawImage(photo, whole);
        void Grey(DrawingContext dc)
        {
            Photo(dc);
            dc.DrawGeometry(PlainFace, null, Polygon(FaceOutline));
        }

        var styles = new (string Name, Rect View, Action<DrawingContext, int> Draw)[]
        {
            ("photo", whole, (dc, _) => Photo(dc)),
            ("photo_grey", whole, (dc, _) => Grey(dc)),
            ("photo_grey_close", CloseUp, (dc, _) => Grey(dc)),
            ("vector", whole, (dc, n) => DrawVector(dc, skin, n)),
        };
        var rendered = new Dictionary<string, List<BitmapSource>>();
        foreach (var (name, view, draw) in styles)
        {
            var frames = Sizes.Select(n => Square(n, view, draw)).ToList();
            for (int i = 0; i < Sizes.Length; i++)
                SavePng(frames[i], Path.Combine(folder, $"{name}_{Sizes[i]}.png"));
            rendered[name] = frames;
        }

        var icons = new (string Name, string Large, string Small)[]
        {
            ("photo", "photo", "photo"),
            ("photo+grey", "photo", "photo_grey"),
            ("photo+close", "photo", "photo_grey_close"),
            ("vector", "vector", "vector"),
        };
        var sheet = new List<(string Name, List<BitmapSource> Frames)>();
        foreach (var (name, large, small) in icons)
        {
            var frames = Sizes.Select((n, i) => rendered[n <= SmallSize ? small : large][i]).ToList();
            WriteIco(Path.Combine(folder, $"{name}.ico"), frames.Select(EncodePng).ToList());
            sheet.Add((name, frames));
        }
        SavePng(CompareSheet(sheet), Path.Combine(folder, "compare.png"));
    }

    private static readonly Brush PlainFace = Frozen(new LinearGradientBrush(Rgb(0x8E, 0x9B, 0xA0), Rgb(0x7A, 0x87, 0x8C), 90));

    private static Geometry Polygon(Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], true, true);
            g.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>The overlay's faceplate, powered and running, rendered large enough to shrink from.</summary>
    private static BitmapSource PhotoFaceplate(SkinLayout skin)
    {
        var panel = new OcuPanel();
        var controller = new RclController(new RclSettings());
        var view = new FaceplateView(panel) { Controller = controller, ShowAntenna = false };
        panel.PowerOn = true;
        for (int i = 0; i < 150; i++)
            controller.Update(0.1, panel.Snapshot(), null, false);
        panel.Dir = Direction.Fwd;
        panel.Speed = SpeedSelect.Four;
        view.DisplayOverride = "4.0";
        view.Settle();

        var size = new Size(view.Width, view.Height);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.UpdateLayout();
        const double scale = 3;
        var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var unit = new CroppedBitmap(bitmap, new Int32Rect(0, (int)(FaceplateView.StripRise * scale), (int)(skin.Width * scale), (int)(skin.Height * scale)));
        unit.Freeze();
        return unit;
    }

    /// <summary>Fits <paramref name="view"/> (in skin units) into a square icon, centred.</summary>
    private static BitmapSource Square(int n, Rect view, Action<DrawingContext, int> draw)
    {
        double scale = n / Math.Max(view.Width, view.Height);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, n, n)));
            dc.PushTransform(new TranslateTransform((n - view.Width * scale) / 2, (n - view.Height * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-view.X, -view.Y));
            dc.PushClip(new RectangleGeometry(view));
            draw(dc, n);
        }
        var bitmap = new RenderTargetBitmap(n, n, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // ---- Vector drawing, in the skin's units so it lines up with the photo ----

    private static readonly Brush CaseMetal = Frozen(new LinearGradientBrush(Rgb(0xAE, 0xB2, 0xB6), Rgb(0x6C, 0x70, 0x74), 90));
    private static readonly Brush CaseLip = Frozen(new SolidColorBrush(Rgb(0x30, 0x33, 0x37)));
    private static readonly Brush Paint = Frozen(new LinearGradientBrush(Rgb(0xF0, 0xC8, 0x5C), Rgb(0xCC, 0x9A, 0x2A), 90));
    private static readonly Brush Sealant = Frozen(new SolidColorBrush(Rgb(0xE6, 0xDB, 0xB0)));
    private static readonly Brush Face = Frozen(new LinearGradientBrush(Rgb(0x62, 0x6E, 0x78), Rgb(0x47, 0x51, 0x5A), 90));
    private static readonly Brush Legend = Frozen(new SolidColorBrush(Rgb(0xEC, 0xEC, 0xEC)));
    private static readonly Brush Chrome = Frozen(new LinearGradientBrush(new GradientStopCollection
        { new(Rgb(0x70, 0x75, 0x7A), 0), new(Rgb(0xF4, 0xF6, 0xF8), 0.45), new(Rgb(0x86, 0x8B, 0x90), 1) }, 0));
    private static readonly Brush Steel = Frozen(new LinearGradientBrush(Rgb(0xD8, 0xDB, 0xDE), Rgb(0x84, 0x88, 0x8C), 90));
    private static readonly Brush PaddleCap = Frozen(new LinearGradientBrush(Rgb(0x9A, 0x9E, 0xA3), Rgb(0x5C, 0x60, 0x64), 90));
    private static readonly Brush Rib = Frozen(new SolidColorBrush(Rgb(0x45, 0x48, 0x4C)));
    private static readonly Brush Dome = Frozen(new RadialGradientBrush(Rgb(0x55, 0x55, 0x58), Rgb(0x08, 0x08, 0x0A)) { GradientOrigin = new Point(0.38, 0.32) });
    private static readonly Brush KnobBody = Frozen(new LinearGradientBrush(Rgb(0x40, 0x40, 0x42), Rgb(0x10, 0x10, 0x12), 0));
    private static readonly Brush LampOff = Frozen(new SolidColorBrush(Rgb(0x9C, 0xA2, 0x9C)));
    private static readonly Brush LampOn = Frozen(new SolidColorBrush(Rgb(0xFF, 0x3C, 0x24)));
    private static readonly Brush LampGlow = Frozen(new RadialGradientBrush(Color.FromArgb(0xA0, 0xFF, 0x30, 0x18), Color.FromArgb(0, 0xFF, 0x30, 0x18)));
    private static readonly Brush DisplayGlass = Frozen(new SolidColorBrush(Rgb(0x2C, 0x04, 0x08)));
    private static readonly Brush DisplayLit = Frozen(new SolidColorBrush(Rgb(0xD8, 0x2A, 0x1C)));
    private static readonly Brush DotOn = Frozen(new SolidColorBrush(Rgb(0xFF, 0x4A, 0x34)));
    private static readonly Brush DotOff = Frozen(new SolidColorBrush(Rgb(0x4E, 0x0E, 0x14)));

    private static void DrawVector(DrawingContext dc, SkinLayout skin, int n)
    {
        // Below 64 px the legend lines, unlit lamps and display dots are smaller than a pixel.
        bool fine = n >= 64;
        double px = skin.Width / n;
        var edge = new Pen(new SolidColorBrush(Rgb(0x26, 0x28, 0x2B)), Math.Clamp(0.6 * px, 1.5, 12));
        var thin = new Pen(edge.Brush, Math.Clamp(0.4 * px, 1, 6));

        Knob(dc, new Point(39.5, 142), 5.3, thin);
        Knob(dc, new Point(703.5, 148.6), -5.3, thin);

        var shell = new RectangleGeometry(new Rect(42, 30, 650, 276), 26, 26);
        dc.DrawGeometry(CaseMetal, edge, shell);
        dc.PushClip(shell);
        dc.DrawRectangle(CaseLip, null, new Rect(0, 288, skin.Width, 30));
        dc.Pop();
        dc.DrawRoundedRectangle(Paint, null, new Rect(54, 36, 626, 80), 16, 16);

        // Hardware on the top edge.
        dc.DrawRoundedRectangle(Steel, thin, new Rect(244, 17, 14, 77), 4, 4);
        dc.DrawRoundedRectangle(Steel, thin, new Rect(483, 20, 19, 74), 4, 4);
        dc.DrawRoundedRectangle(Steel, thin, new Rect(339, 4, 65, 90), 8, 8);
        dc.DrawEllipse(Chrome, thin, new Point(371.5, 50), 11, 11);
        if (fine)
            dc.DrawEllipse(Rib, null, new Point(371.5, 50), 4, 4);
        foreach (var name in new[] { "auto", "dir" })
            Paddle(dc, Pivot(skin.Controls[name]), fine, thin);
        foreach (var name in new[] { "bell", "time" })
            Toggle(dc, Pivot(skin.Controls[name]), thin);
        foreach (var b in skin.Buttons.Values)
        {
            var c = new Point(b.Center[0], b.Center[1]);
            dc.DrawEllipse(Steel, thin, c, b.Bezel, b.Bezel);
            dc.DrawEllipse(Dome, null, c, b.Dome, b.Dome);
        }

        // Face panel.
        dc.DrawRoundedRectangle(Sealant, null, new Rect(55, 108, 626, 184), 12, 12);
        dc.DrawRoundedRectangle(Face, null, new Rect(61, 114, 614, 172), 8, 8);
        if (fine)
        {
            var line = new Pen(Legend, 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            foreach (var (a, b) in new (Point, Point)[]
            {
                (new(167, 126), new(167, 262)), (new(252, 116), new(252, 278)), (new(66, 277), new(252, 277)),
                (new(252, 177), new(366, 177)), (new(366, 116), new(366, 177)),
                (new(400, 116), new(400, 176)), (new(400, 176), new(497, 176)), (new(497, 116), new(497, 176)),
                (new(601, 116), new(541, 276)), (new(541, 277), new(670, 277)),
            })
                dc.DrawLine(line, a, b);
        }

        Lamps(dc, skin.Lamps["ind"], IndexOf(IndBrake.Rel), fine);
        Lamps(dc, skin.Lamps["auto"], IndexOf(AutoBrake.Rel), fine);
        Lamps(dc, skin.Lamps["dir"], IndexOf(Direction.Fwd), fine);
        Lamps(dc, skin.Lamps["speed"], IndexOf(SpeedSelect.Four), fine);
        Display(dc, skin.Display, fine);
    }

    // Where a lever goes into its nut: the bottom middle of everything it covers.
    private static Point Pivot(SkinControl control) => new(control.Hit.X + control.Hit.W / 2, control.Hit.Y + control.Hit.H);

    private static void Knob(DrawingContext dc, Point centre, double lean, Pen pen)
    {
        dc.PushTransform(new RotateTransform(lean, centre.X, centre.Y));
        dc.DrawRoundedRectangle(KnobBody, pen, new Rect(centre.X - 17, centre.Y - 39, 34, 78), 10, 10);
        dc.DrawLine(new Pen(Brushes.Gainsboro, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            new Point(centre.X - 10, centre.Y), new Point(centre.X + 10, centre.Y));
        dc.Pop();
    }

    private static void Paddle(DrawingContext dc, Point pivot, bool fine, Pen pen)
    {
        dc.DrawRoundedRectangle(Steel, pen, new Rect(pivot.X - 24, pivot.Y - 6, 48, 22), 4, 4);
        dc.DrawRectangle(Chrome, null, new Rect(pivot.X - 9, pivot.Y - 22, 18, 17));
        var cap = new Rect(pivot.X - 22, pivot.Y - 58, 44, 38);
        dc.DrawRoundedRectangle(PaddleCap, pen, cap, 6, 6);
        if (!fine)
            return;
        var rib = new Pen(Rib, 2.4);
        for (int i = 1; i <= 5; i++)
        {
            double y = cap.Top + cap.Height * i / 6;
            dc.DrawLine(rib, new Point(cap.Left + 6, y), new Point(cap.Right - 6, y));
        }
    }

    private static void Toggle(DrawingContext dc, Point p, Pen pen)
    {
        const double ball = 8.2, half = 6.4;
        double ballY = p.Y - 42.9, baseY = p.Y - 14.2;
        dc.DrawRoundedRectangle(Steel, pen, new Rect(p.X - 22, p.Y - 8, 44, 24), 4, 4);
        dc.DrawRectangle(Chrome, null, new Rect(p.X - 3, baseY, 6, p.Y - 8 - baseY));
        var bat = new StreamGeometry();
        using (var g = bat.Open())
        {
            g.BeginFigure(new Point(p.X - half, baseY), true, true);
            g.LineTo(new Point(p.X - ball * 0.7, ballY), true, false);
            g.LineTo(new Point(p.X + ball * 0.7, ballY), true, false);
            g.LineTo(new Point(p.X + half, baseY), true, false);
        }
        dc.DrawGeometry(Chrome, null, bat);
        dc.DrawEllipse(Chrome, null, new Point(p.X, baseY), half, half * 0.6);
        dc.DrawEllipse(Chrome, pen, new Point(p.X, ballY), ball, ball);
    }

    private static void Lamps(DrawingContext dc, SkinLampGroup group, int lit, bool fine)
    {
        for (int i = 0; i < group.Points.Length; i++)
        {
            var c = new Point(group.Points[i][0], group.Points[i][1]);
            if (i == lit)
            {
                dc.DrawEllipse(LampGlow, null, c, 14, 14);
                dc.DrawEllipse(LampOn, null, c, fine ? 5 : 8, fine ? 5 : 8);
            }
            else if (fine)
            {
                dc.DrawEllipse(LampOff, null, c, 4.5, 4.5);
            }
        }
    }

    private static void Display(DrawingContext dc, SkinRect r, bool fine)
    {
        var rect = new Rect(r.X, r.Y, r.W, r.H);
        if (!fine)
        {
            dc.DrawRoundedRectangle(DisplayLit, null, rect, 2, 2);
            return;
        }
        dc.DrawRoundedRectangle(DisplayGlass, null, rect, 2, 2);
        const string text = "  40";
        const int pointAfter = 2;
        double pad = 3, gap = 2.2;
        double moduleW = (r.W - 2 * pad - 3 * gap) / 4;
        double pitch = Math.Min(moduleW / DotMatrixFont.Width, (r.H - 2 * pad) / DotMatrixFont.Rows);
        double top = r.Y + (r.H - pitch * DotMatrixFont.Rows) / 2;
        for (int m = 0; m < 4; m++)
        {
            double left = r.X + pad + m * (moduleW + gap) + (moduleW - pitch * DotMatrixFont.Width) / 2;
            var glyph = DotMatrixFont.Cell(text[m]);
            for (int col = 0; col < DotMatrixFont.Width; col++)
                for (int row = 0; row < DotMatrixFont.Rows; row++)
                {
                    bool on = (glyph[col] >> row & 1) != 0;
                    dc.DrawEllipse(on ? DotOn : DotOff, null,
                        new Point(left + (col + 0.5) * pitch, top + (row + 0.5) * pitch), pitch * 0.4, pitch * 0.4);
                }
            if (m == pointAfter)
                dc.DrawEllipse(DotOn, null, new Point(left + DotMatrixFont.Width * pitch + gap / 2, top + (DotMatrixFont.Rows - 0.5) * pitch), pitch * 0.4, pitch * 0.4);
        }
    }

    // ---- Output ----

    private static BitmapSource CompareSheet(List<(string Name, List<BitmapSource> Frames)> sets)
    {
        const double margin = 24, labelW = 120, zoom = 4;
        double colsW = Sizes.Sum() + margin * Sizes.Length;
        double zoomW = (32 + 16) * zoom + margin * 3;
        double width = margin + labelW + colsW + zoomW, rowH = 256 + margin;
        var backgrounds = new[] { Rgb(0x20, 0x20, 0x20), Rgb(0xF3, 0xF3, 0xF3) };
        double height = margin + rowH * sets.Count * backgrounds.Length + 20;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        var font = new Typeface("Segoe UI");
        using (var dc = visual.RenderOpen())
        {
            double y = margin;
            foreach (var bg in backgrounds)
            {
                var ink = new SolidColorBrush(bg.R < 128 ? Colors.White : Colors.Black);
                dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, y - margin / 2, width, rowH * sets.Count));
                foreach (var (name, frames) in sets)
                {
                    dc.DrawText(new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 16, ink, 1),
                        new Point(margin, y + 120));
                    double x = margin + labelW;
                    for (int i = 0; i < Sizes.Length; i++)
                    {
                        dc.DrawImage(frames[i], new Rect(x, y + (256 - Sizes[i]) / 2.0, Sizes[i], Sizes[i]));
                        x += Sizes[i] + margin;
                    }
                    x += margin;
                    foreach (int n in new[] { 32, 16 })
                    {
                        var frame = frames[Array.IndexOf(Sizes, n)];
                        dc.DrawImage(frame, new Rect(x, y + (256 - n * zoom) / 2, n * zoom, n * zoom));
                        x += n * zoom + margin;
                    }
                    y += rowH;
                }
            }
        }
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] EncodePng(BitmapSource frame)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void SavePng(BitmapSource frame, string path) => File.WriteAllBytes(path, EncodePng(frame));

    /// <summary>Writes a Windows icon whose images are PNG-compressed, largest first.</summary>
    private static void WriteIco(string path, IList<byte[]> pngs)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)pngs.Count);
        int offset = 6 + 16 * pngs.Count;
        for (int i = 0; i < pngs.Count; i++)
        {
            byte dim = (byte)(Sizes[i] >= 256 ? 0 : Sizes[i]);
            writer.Write(dim);
            writer.Write(dim);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(pngs[i].Length);
            writer.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs)
            writer.Write(png);
    }

    private static int IndexOf<T>(T value) where T : struct, Enum => Array.IndexOf(Enum.GetValues<T>(), value);
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
