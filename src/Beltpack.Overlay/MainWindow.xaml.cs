using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Beltpack.Core.Rcl;
using Beltpack.Core.Run8;
using Beltpack.Core.Talk;

namespace Beltpack.Overlay;

public partial class MainWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly OcuPanel _panel = new();
    private readonly FaceplateView _view;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<Key> _keysDown = new();
    private readonly KeyboardHook _hook;
    private readonly TraceLog _log;
    private readonly Run8LogWatcher _run8Log;

    private OverlaySettings _settings = null!;
    private RclController _controller = null!;
    private CommandSender _sender = null!;
    private Run8Link? _link;
    private string? _linkError;
    private SoundEngine? _sound;
    private Talker? _talker;
    private Dictionary<Key, string> _bindings = new();
    private bool _beltpack;

    public MainWindow()
    {
        CrashLog.Step("keyboard hook");
        _hook = new KeyboardHook();
        CrashLog.Step("events.log");
        _log = new TraceLog();
        CrashLog.Step("Run8.log watcher");
        _run8Log = new Run8LogWatcher();
        CrashLog.Step("window layout");
        InitializeComponent();
        CrashLog.Step("faceplate");
        _view = new FaceplateView(_panel);
        Root.Children.Add(_view);

        _view.ChangeoverClicked += ToggleChangeover;
        _view.BackgroundDrag += () =>
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed)
                DragMove();
        };
        _hook.KeyChanged = OnKey;
        MouseRightButtonUp += (_, e) =>
        {
            if (!e.Handled)
                ShowMenu();
        };
        PreviewMouseWheel += OnPreviewMouseWheel;

        CrashLog.Step("settings");
        ApplySettings(OverlaySettings.Load());
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(WarnIfTalkerMissing, DispatcherPriority.ApplicationIdle);
    }

    private void WarnIfTalkerMissing()
    {
        if (_talker is null || _talker.UsesRecordings || !_settings.TalkerEnabled || !_settings.WarnIfTalkerMissing)
            return;
        _log.Note($"talker: recordings not used: {_talker.RecordingsProblem}");
        var answer = MessageBox.Show(this,
            $"The talker recordings weren't found, so the talker will use a Windows voice instead.\n\n"
            + $"{_talker.RecordingsProblem.TrimEnd('.')}. It comes with the program and needs to be in the same folder:\n"
            + $"{AppContext.BaseDirectory}\n\n"
            + "Remind me next time?",
            "Talker recordings", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.No)
        {
            _settings.WarnIfTalkerMissing = false;
            _settings.Save();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        CrashLog.Step("window created by Windows");
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        CrashLog.Step("window style set");
    }

    private void ApplySettings(OverlaySettings settings)
    {
        _settings = settings;

        _link?.Dispose();
        _link = null;
        _linkError = null;
        CrashLog.Step("Run8 link");
        try
        {
            _link = new Run8Link(settings.Rcl.Run8Port);
        }
        catch (SocketException)
        {
            _linkError = $"Port {settings.Rcl.Run8Port + 1} is in use by another program";
        }

        _run8Log.ConfiguredFolder = settings.Run8Folder;
        _controller = new RclController(settings.Rcl);
        _controller.Sound += s => _sound?.Play(s);
        _controller.Talker += s => _talker?.Say(s);
        _sender = new CommandSender((type, value) => _link?.Send(type, value));

        CrashLog.Step("sound");
        _sound?.Dispose();
        _sound = new SoundEngine(settings.SoundVolume);
        CrashLog.Step("talker");
        _talker?.Dispose();
        _talker = new Talker(settings.TalkerVolume) { Enabled = settings.TalkerEnabled };
        CrashLog.Step("talker ready");
        _talker.Missing += m => _log.Note($"talker: no recording for \"{m}\"");
        _talker.Failed += m => _log.Note($"talker: failed: {m}");

        _bindings = new Dictionary<Key, string>();
        foreach (var (action, keyName) in settings.Keys)
            if (Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
                _bindings[key] = action;

        ScaleXf.ScaleX = ScaleXf.ScaleY = Math.Clamp(settings.Scale, 0.4, 3);
        Opacity = Math.Clamp(settings.Opacity, 0.2, 1);
        _view.ShowAntenna = settings.ShowAntenna;
        if (settings.WindowLeft is double left && settings.WindowTop is double top)
        {
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void Tick()
    {
        double dt = Math.Min(_clock.Elapsed.TotalSeconds, 0.25);
        _clock.Restart();

        var input = _panel.Snapshot();
        var telemetry = _link?.Latest;
        bool linkOk = _link is not null && _link.TelemetryAge.TotalSeconds < _settings.Rcl.CommLossSeconds;

        _controller.ControllingUnit = _settings.LocomotiveFromRun8 ? _run8Log.ControllingUnit : null;
        _controller.Update(dt, input, linkOk ? telemetry : null, linkOk);
        if (_beltpack)
            _sender.Send(_controller.Command, dt, linkOk ? telemetry : null);
        _log.Record(_controller, input, _beltpack, linkOk ? telemetry : null);
        _sound?.SetAlarm(_controller.Alarm);

        _view.Controller = _controller;
        _view.Beltpack = _beltpack;
        _view.LinkOk = linkOk;
        _view.LinkText = _linkError
            ?? (linkOk && telemetry is not null
                ? $"Run8  {telemetry.SpeedMph:0.0} mph  BP {telemetry.BrakePipePsi}  BC {telemetry.BrakeCylinderPsi}"
                : $"No data from Run8 (port {_settings.Rcl.Run8Port})");
        _view.Tick(dt);
    }

    private void ToggleChangeover()
    {
        _beltpack = !_beltpack;
        _sender.Reset();
        _talker?.Say(_beltpack ? "Locomotive in remote mode." : "Changeover to manual.");
    }

    private bool OnKey(Key key, bool down)
    {
        if (!_bindings.TryGetValue(key, out var action))
            return false;

        bool repeat = down && !_keysDown.Add(key);
        if (!down)
            _keysDown.Remove(key);
        if (repeat)
            return _settings.SwallowBoundKeys;

        var p = _panel;
        switch (action)
        {
            case KeyAction.SpeedUp when down: p.StepSpeed(1); break;
            case KeyAction.SpeedDown when down: p.StepSpeed(-1); break;
            case KeyAction.DirForward when down: p.StepDir(1); break;
            case KeyAction.DirReverse when down: p.StepDir(-1); break;
            case KeyAction.AutoApply when down: p.StepAuto(1); break;
            case KeyAction.AutoRelease when down: p.StepAuto(-1); break;
            case KeyAction.IndApply when down: p.StepInd(1); break;
            case KeyAction.IndRelease when down: p.StepInd(-1); break;
            case KeyAction.Rsc: p.SetButton(nameof(OcuPanel.Rsc), down); break;
            case KeyAction.Status: p.SetButton(nameof(OcuPanel.Status), down); break;
            case KeyAction.TiltTimeout: p.SetButton(nameof(OcuPanel.TiltTimeout), down); break;
            case KeyAction.Pitch: p.SetButton(nameof(OcuPanel.Pitch), down); break;
            case KeyAction.Sand: p.SetButton(nameof(OcuPanel.Sand), down); break;
            case KeyAction.Horn: p.SetHorn(down); break;
            case KeyAction.Bell when down: p.ToggleBell(); break;
            case KeyAction.Headlight when down: p.HeadlightBright = !p.HeadlightBright; p.Notify(); break;
            case KeyAction.Brightness when down: p.StepBrightness(); break;
            // TILT DISABLED for the v1.2 play-test build.
            // case KeyAction.Tilt when down: p.Tilted = !p.Tilted; p.Notify(); break;
            case KeyAction.Power when down: p.PowerOn = !p.PowerOn; p.Notify(); break;
            case KeyAction.Changeover when down: ToggleChangeover(); break;
        }
        return _settings.SwallowBoundKeys;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;
        SetScale(ScaleXf.ScaleX + (e.Delta > 0 ? 0.05 : -0.05));
        e.Handled = true;
    }

    private void SetScale(double scale)
    {
        scale = Math.Clamp(scale, 0.4, 3);
        ScaleXf.ScaleX = ScaleXf.ScaleY = scale;
        _settings.Scale = scale;
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(_beltpack ? "Changeover to MANUAL" : "Changeover to BELTPACK", ToggleChangeover));
        menu.Items.Add(Item(_panel.PowerOn ? "Turn OCU off" : "Turn OCU on", () => { _panel.PowerOn = !_panel.PowerOn; _panel.Notify(); }));
        menu.Items.Add(new Separator());

        var talker = Item("Talker voice", () =>
        {
            _settings.TalkerEnabled = !_settings.TalkerEnabled;
            if (_talker is not null)
                _talker.Enabled = _settings.TalkerEnabled;
        });
        talker.IsChecked = _settings.TalkerEnabled;
        menu.Items.Add(talker);

        var talkerVolume = new MenuItem { Header = "Talker volume" };
        foreach (var percent in new[] { 100, 75, 50, 25 })
        {
            var item = Item($"{percent}%", () =>
            {
                _settings.TalkerVolume = percent;
                if (_talker is not null)
                    _talker.Volume = percent;
                _settings.Save();
            });
            item.IsChecked = _settings.TalkerVolume == percent;
            talkerVolume.Items.Add(item);
        }
        menu.Items.Add(talkerVolume);

        var railroad = new MenuItem { Header = "Railroad" };
        foreach (var name in TalkerScript.RecordedRailroads.Prepend(""))
        {
            var item = Item(name == "" ? "None" : name, () =>
            {
                _settings.Rcl.Railroad = name;
                _settings.Save();
            });
            item.IsChecked = string.Equals(_settings.Rcl.Railroad.Trim(), name, StringComparison.OrdinalIgnoreCase);
            railroad.Items.Add(item);
        }
        menu.Items.Add(railroad);

        var size = new MenuItem { Header = "Size" };
        foreach (var s in new[] { 0.6, 0.8, 1.0, 1.25, 1.5, 2.0 })
            size.Items.Add(Item($"{s:P0}", () => SetScale(s)));
        menu.Items.Add(size);

        var opacity = new MenuItem { Header = "Opacity" };
        foreach (var o in new[] { 1.0, 0.9, 0.75, 0.6, 0.45 })
            opacity.Items.Add(Item($"{o:P0}", () => { Opacity = o; _settings.Opacity = o; }));
        menu.Items.Add(opacity);

        var antenna = Item("Antenna", () => _view.ShowAntenna = _settings.ShowAntenna = !_settings.ShowAntenna);
        antenna.IsChecked = _settings.ShowAntenna;
        menu.Items.Add(antenna);

        menu.Items.Add(Item("Key bindings…", ShowBindings));
        menu.Items.Add(Item("Open settings file", () =>
        {
            SaveWindowState();
            Process.Start(new ProcessStartInfo(OverlaySettings.FilePath) { UseShellExecute = true });
        }));
        menu.Items.Add(Item("Reload settings", () => ApplySettings(OverlaySettings.Load())));
        menu.Items.Add(Item("About…", ShowAbout));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", Close));
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowAbout()
    {
        var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0];
        MessageBox.Show(this,
            $"Run8 OCU II {version}\n"
            + (CrashLog.SoftwareRendering ? "Diagnostic build: drawn without the graphics card\n" : "")
            + "CANAC / Cattron Beltpack OCU II emulator for Run8 V3\n\n"
            + "Designed, photographed and tested by Nathan French.\n"
            + "Written together with Cursor's AI coding agent, November 2026.\n"
            + (_talker?.UsesRecordings == true ? "Talker audio: Christian Currao (SketchyBlueFlag), used with permission.\n" : "")
            + "\ngithub.com/NatFrench/Run8-OCU-II\n"
            + "Free software under the GNU GPL v3.0"
            + (_talker?.UsesRecordings == true ? " (the talker audio is not covered by it)" : ""),
            "About");
    }

    private void ShowBindings()
    {
        var lines = _settings.Keys.Select(kv => $"{kv.Key,-12} {kv.Value}");
        MessageBox.Show(this,
            "Keys work while Run8 or this window has focus (NumLock on).\n\n" + string.Join("\n", lines)
            + $"\n\nEdit them in:\n{OverlaySettings.FilePath}",
            "Beltpack key bindings");
    }

    private void SaveWindowState()
    {
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        SaveWindowState();
        _hook.Dispose();
        _link?.Dispose();
        _sound?.Dispose();
        _talker?.Dispose();
        _log.Dispose();
        _run8Log.Dispose();
        base.OnClosed(e);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
