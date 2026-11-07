using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Beltpack.Overlay;

/// <summary>
/// startup.log records each step of starting up as it happens, so the last line shows where the
/// program got to even when the .NET runtime itself dies and nothing else can be written. Errors the
/// program can catch go to crash.log, with the startup steps and the end of events.log, and a
/// message box says where to find it.
/// </summary>
public static class CrashLog
{
#if SOFTWARE_RENDER
    public static readonly bool SoftwareRendering = true;
#else
    public static readonly bool SoftwareRendering = false;
#endif

    private static readonly string Folder = Path.GetDirectoryName(OverlaySettings.FilePath)!;
    private static readonly string Suffix = SoftwareRendering ? "-software" : "";
    public static readonly string StartupPath = Path.Combine(Folder, $"startup{Suffix}.log");
    public static readonly string CrashPath = Path.Combine(Folder, $"crash{Suffix}.log");
    private static int _crashed;

    public static string Version => typeof(CrashLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion.Split('+')[0] ?? "?";

    public static void Install(Application app)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(StartupPath, Header());
        }
        catch (Exception)
        {
        }
        if (!SoftwareRendering)
        {
            Step("asking the graphics card what it can do");
            Step($"graphics card drawing tier {RenderCapability.Tier >> 16} of 2");
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crashed(e.ExceptionObject as Exception, "Unhandled error");
        app.DispatcherUnhandledException += (_, e) => Crashed(e.Exception, "Unhandled error on the window thread");
        TaskScheduler.UnobservedTaskException += (_, e) => Write(e.Exception, "Unobserved background error");
    }

    public static void Step(string what)
    {
        try
        {
            File.AppendAllText(StartupPath, $"{DateTime.Now:HH:mm:ss.fff} {what}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }

    private static string Header() =>
        $"Run8 OCU II {Version}, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"
            + $"Drawing: {(SoftwareRendering ? "software only (graphics card not used)" : "with the graphics card")}{Environment.NewLine}"
            + $"Windows: {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}, .NET {Environment.Version}{Environment.NewLine}"
            + $"Program: {Environment.ProcessPath}{Environment.NewLine}{Environment.NewLine}";

    private static void Crashed(Exception? ex, string what)
    {
        if (Interlocked.Exchange(ref _crashed, 1) != 0)
            return;
        Write(ex, what);
        try
        {
            MessageBox.Show(
                $"The OCU stopped because of an error:\n\n{ex?.Message}\n\nThe details are in\n{CrashPath}\n\n"
                + "Please send that file to whoever gave you the program.",
                "Run8 OCU II", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
        }
    }

    private static void Write(Exception? ex, string what)
    {
        try
        {
            string startup = File.Exists(StartupPath) ? File.ReadAllText(StartupPath) : "(no startup.log)\n";
            File.WriteAllText(CrashPath,
                $"{what} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}"
                + $"--- startup.log{Environment.NewLine}{startup}{Environment.NewLine}"
                + $"--- end of events.log{Environment.NewLine}{string.Join(Environment.NewLine, EventsTail(40))}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }

    private static IEnumerable<string> EventsTail(int lines)
    {
        try
        {
            using var stream = new FileStream(TraceLog.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var tail = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                tail.Enqueue(line);
                if (tail.Count > lines)
                    tail.Dequeue();
            }
            return tail;
        }
        catch (Exception)
        {
            return ["(events.log could not be read)"];
        }
    }

    /// <summary>Notes the first frame actually drawn, and that the program is still alive a while later.</summary>
    public static void WatchFirstFrames()
    {
        void OnFrame(object? sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnFrame;
            Step("first frame drawn");
        }
        CompositionTarget.Rendering += OnFrame;

        var alive = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        int ticks = 0;
        alive.Tick += (_, _) =>
        {
            Step($"still running after {++ticks * 10} s");
            if (ticks >= 6)
                alive.Stop();
        };
        alive.Start();
    }
}
