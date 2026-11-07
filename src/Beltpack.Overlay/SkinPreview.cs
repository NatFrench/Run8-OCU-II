using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Beltpack.Core.Ocu;
using Beltpack.Core.Rcl;

namespace Beltpack.Overlay;

/// <summary>
/// Development aid: "Beltpack.Overlay.exe --render-skin &lt;folder&gt;" draws the faceplate in a few
/// control states to PNG files and exits, for checking the skin without running Run8.
/// </summary>
public static class SkinPreview
{
    public static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        var panel = new OcuPanel();
        var controller = new RclController(new RclSettings());
        var view = new FaceplateView(panel) { Controller = controller, LinkText = "Preview", LinkOk = true };

        Save(view, folder, "1-off");

        panel.PowerOn = true;
        controller.Update(0.1, panel.Snapshot(), null, false);
        view.Settle(strip: true);
        Save(view, folder, "2-lamp-test");

        for (int i = 0; i < 15; i++)
            controller.Update(0.1, panel.Snapshot(), null, false);
        view.Settle();
        Save(view, folder, "2b-display-test");

        for (int i = 0; i < 100; i++)
            controller.Update(0.1, panel.Snapshot(), null, false);
        panel.Ind = IndBrake.Full;
        panel.Auto = AutoBrake.Med;
        panel.Dir = Direction.Fwd;
        panel.Speed = SpeedSelect.Four;
        panel.BellHorn = BellHorn.Bell;
        panel.HeadlightBright = true;
        view.DisplayOverride = "12.5";
        view.Settle();
        Save(view, folder, "3-running");

        panel.StepBrightness();
        Save(view, folder, "3b-running-medium");
        panel.StepBrightness();
        Save(view, folder, "3c-running-low");
        panel.StepBrightness();

        panel.Ind = IndBrake.Emerg;
        panel.Dir = Direction.Rev;
        panel.Speed = SpeedSelect.Max;
        panel.BellHorn = BellHorn.BellHorn;
        panel.TiltTimeout = true;
        panel.Rsc = true;
        view.DisplayOverride = "RSC9";
        view.Settle(autoHeld: 1);
        Save(view, folder, "4-swung");

        panel.Ind = IndBrake.Rel;
        panel.Dir = Direction.Neutral;
        panel.Speed = SpeedSelect.Stop;
        panel.BellHorn = BellHorn.Off;
        panel.TiltTimeout = false;
        panel.Status = true;
        panel.Rsc = false;
        panel.Pitch = true;
        view.DisplayOverride = "RSC";
        view.Settle(autoHeld: -1);
        Save(view, folder, "5-other-way");

        RenderMotion(panel, view, Path.Combine(folder, "motion"));
    }

    /// <summary>
    /// "--render-unit &lt;folder&gt;": the unit alone on a transparent background at 4x, powered off
    /// and powered on, for labelled diagrams.
    /// </summary>
    public static void RenderUnit(string folder)
    {
        Directory.CreateDirectory(folder);
        var panel = new OcuPanel();
        var controller = new RclController(new RclSettings());
        var view = new FaceplateView(panel) { Controller = controller };
        view.Settle();
        SaveUnit(view, folder, "unit-off");

        panel.PowerOn = true;
        for (int i = 0; i < 150; i++)
            controller.Update(0.1, panel.Snapshot(), null, false);
        view.DisplayOverride = "0.0";
        view.Settle();
        SaveUnit(view, folder, "unit-on");
    }

    private static void SaveUnit(FaceplateView view, string folder, string name)
    {
        const double scale = 4;
        var size = new Size(view.Width, view.Height);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.InvalidateVisual();
        view.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);
        int width = bitmap.PixelWidth;
        var pixels = new byte[width * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        int first = Array.FindIndex(pixels, b => b != 0) / 4;
        int top = Math.Min(first / width, (int)(FaceplateView.StripRise * scale));
        var unit = new CroppedBitmap(bitmap, new Int32Rect(0, top, bitmap.PixelWidth, bitmap.PixelHeight - top));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(unit));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    /// <summary>Works every moving part through its positions, saving 30 frames a second.</summary>
    private static void RenderMotion(OcuPanel panel, FaceplateView view, string folder)
    {
        Directory.CreateDirectory(folder);
        panel.Status = panel.Pitch = false;
        panel.Ind = IndBrake.Rel;
        panel.Auto = AutoBrake.Rel;
        panel.Dir = Direction.Neutral;
        panel.Speed = SpeedSelect.Stop;
        panel.BellHorn = BellHorn.Off;
        view.DisplayOverride = "0.0";
        view.Settle();

        var events = new List<(double Time, Action Act)>
        {
            (1.1, () => panel.Dir = Direction.Fwd),
            (1.6, () => panel.BellHorn = BellHorn.Bell),
            (2.0, () => panel.BellHorn = BellHorn.BellHorn),
            (2.5, () => panel.BellHorn = BellHorn.Bell),
            (2.8, () => panel.TiltTimeout = true),
            (3.2, () => panel.TiltTimeout = false),
            (3.5, () => panel.Status = true),
            (3.9, () => panel.Status = false),
            (6.3, () => panel.Dir = Direction.Rev),
        };
        for (int i = 1; i <= 4; i++)
        {
            int step = i;
            events.Add((4.2 + 0.3 * step, () => panel.Ind = (IndBrake)step));
        }
        for (int i = 1; i <= 7; i++)
        {
            int step = i;
            events.Add((4.2 + 0.25 * step, () => panel.Speed = (SpeedSelect)step));
        }

        const double dt = 1 / 30.0;
        for (int frame = 0; frame < 225; frame++)
        {
            double t = frame * dt;
            foreach (var (time, act) in events)
                if (time > t - dt && time <= t)
                    act();
            int autoHeld = t is >= 0.3 and < 0.8 ? 1 : t is >= 6.6 and < 7.0 ? -1 : 0;
            view.PreviewStep(autoHeld, dt);
            Save(view, folder, $"f{frame:000}", 1.5);
        }
    }

    private static void Save(FaceplateView view, string folder, string name, double scale = 2)
    {
        var size = new Size(view.Width, view.Height);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.InvalidateVisual();
        view.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x5C, 0x7A, 0x52)), null, new Rect(size));
        bitmap.Render(backdrop);
        bitmap.Render(view);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }
}
