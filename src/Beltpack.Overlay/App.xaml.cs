using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Beltpack.Overlay;

// Run8 OCU II: a Beltpack OCU II for Run8 V3.
// Designed, photographed and tested by Nathan French; written together with Cursor's AI coding
// agent over the first nights of November 2026.
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--render-skin")
        {
            SkinPreview.Render(e.Args[1]);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--render-unit")
        {
            SkinPreview.RenderUnit(e.Args[1]);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--render-talker")
        {
            TalkerPreview.Render(e.Args[1]);
            Shutdown();
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--say")
        {
            TalkerPreview.Say(e.Args[1], e.Args[2]);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--render-icons")
        {
            IconRender.Render(e.Args[1]);
            Shutdown();
            return;
        }

#if SOFTWARE_RENDER
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
#endif
        CrashLog.Install(this);
        CrashLog.Step("creating the window");
        var window = new MainWindow();
        CrashLog.Step("showing the window");
        window.Show();
        CrashLog.Step("window shown");
        CrashLog.WatchFirstFrames();
    }
}
