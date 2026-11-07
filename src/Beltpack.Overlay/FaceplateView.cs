using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Beltpack.Core.Ocu;
using Beltpack.Core.Rcl;

namespace Beltpack.Overlay;

/// <summary>
/// Draws the OCU II-E from the photo skin (see tools/make_skin.py), animates its moving parts, and
/// maps mouse input onto it. Emulator-only controls live on a strip that rises from behind the
/// top of the unit while the mouse is near the top.
/// </summary>
public sealed class FaceplateView : FrameworkElement
{
    // The case's top edge in the skin runs from about 53 to 58 between the rounded shoulders, with
    // the fins and antenna bracket standing up to about y=7 and the toggles to y=2. The strip's
    // bottom stays tucked behind the case and, when out, it rises StripRise above the skin so its
    // buttons clear the hardware. The view reserves that space above the skin (skin y=0 is view
    // y=StripRise). Tucked away, its top sits at StripBottom, where the case is wider than the strip.
    internal const double StripRise = 40;
    private const double StripLeft = 100, StripRight = 606;
    private const double StripBottom = 90;
    private const double StripTop = -StripRise;
    private const double StripHeight = StripBottom - StripTop;
    // Only the top of the antenna bracket (the hood above the jack) raises the strip, so it stays out
    // of the way of the controls; once out, it stays while the mouse is on the part showing above
    // the case's highest edge.
    private const double StripVisibleBottom = 52;
    private static readonly Rect StripTrigger = new(318, 6, 53, 26);
    private const double StripLingerSeconds = 0.5;

    private static readonly Typeface Legend = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface LegendBold = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)));
    private static readonly LampBrushes RedLamp = Lamp(Color.FromRgb(0xFF, 0xE0, 0xD0), Color.FromRgb(0xFF, 0x30, 0x18), Color.FromRgb(0xA0, 0x08, 0x04));
    private static readonly LampBrushes YellowLamp = Lamp(Color.FromRgb(0xFF, 0xF6, 0xD0), Color.FromRgb(0xFF, 0xB8, 0x10), Color.FromRgb(0xA0, 0x64, 0x00));
    private static readonly LampBrushes GreenLamp = Lamp(Color.FromRgb(0xE0, 0xFF, 0xE4), Color.FromRgb(0x30, 0xE8, 0x50), Color.FromRgb(0x06, 0x78, 0x1C));
    private static readonly Brush IconGlow = Freeze(new RadialGradientBrush(Color.FromArgb(0xB0, 0xFF, 0x30, 0x18), Color.FromArgb(0, 0xFF, 0x30, 0x18)));
    private static readonly Brush DisplayBg = Freeze(new LinearGradientBrush(Color.FromRgb(0x2C, 0x04, 0x08), Color.FromRgb(0x48, 0x0A, 0x12), 90));
    private static readonly Brush ModuleFace = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x03, 0x06)));
    private static readonly Brush DotOff = Freeze(new SolidColorBrush(Color.FromRgb(0x52, 0x10, 0x16)));
    private static readonly Brush DotUnpowered = Freeze(new SolidColorBrush(Color.FromRgb(0x3E, 0x0A, 0x10)));
    private static readonly Brush DotOn = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x42, 0x30)));
    private static readonly Brush DotGlow = Freeze(new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0x30, 0x20)));
    private static readonly Brush DomePressed = Freeze(new RadialGradientBrush(Color.FromArgb(0x50, 0, 0, 0), Color.FromArgb(0xB0, 0, 0, 0)));
    private static readonly Pen DomeRim = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), 2.2));
    private static readonly Brush StripBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xE8, 0x26, 0x28, 0x2A)));
    private static readonly Brush StripButton = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x40)));
    private static readonly Brush AccentOn = Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0xC8, 0x5A)));
    private static readonly Brush AccentWarn = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x20)));
    private static readonly Pen DarkPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), 1.5));

    private readonly OcuPanel _panel;
    private readonly SkinLayout _skin;
    private readonly List<Region> _regions = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame;
    private bool _animating;
    private Region? _held;
    private MouseButton _heldButton;
    private double _blinkClock;
    // The corner button held with the mouse; RSC from the keyboard shows on the right one.
    private string? _rscCorner;

    // Each moving part, shown in the position the panel state calls for.
    private readonly PhotoControl _auto, _dir, _bell, _time, _ind, _speed;
    private double _strip;
    private int _autoHeld;
    private int _autoKick;
    private double _autoKickUntil;
    private AutoBrake _lastAuto;
    private double _hoverUntil;
    private bool _stripHover;

    public FaceplateView(OcuPanel panel)
    {
        _panel = panel;
        _skin = SkinLayout.Load();
        _lastAuto = panel.Auto;
        Width = _skin.Width;
        Height = StripRise + _skin.Height;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        var controls = _skin.Controls;
        _auto = new PhotoControl(controls["auto"], AutoPose(Now));
        _dir = new PhotoControl(controls["dir"], DirPose());
        _bell = new PhotoControl(controls["bell"], BellPose());
        _time = new PhotoControl(controls["time"], TimePose());
        _ind = new PhotoControl(controls["ind"], PhotoControl.Detent(IndexOf(panel.Ind)));
        _speed = new PhotoControl(controls["speed"], PhotoControl.Detent(IndexOf(panel.Speed)));
        BuildRegions();
        SnapAnimations();
        Unloaded += (_, _) => StopAnimating();
    }

    public RclController? Controller { get; set; }
    public bool Beltpack { get; set; }
    public string LinkText { get; set; } = "";
    public bool LinkOk { get; set; }

    /// <summary>Draws a stub of the whip antenna on the jack.</summary>
    public bool ShowAntenna
    {
        get => _showAntenna;
        set
        {
            _showAntenna = value;
            InvalidateVisual();
        }
    }
    private bool _showAntenna = true;

    public event Action? ChangeoverClicked;
    public event Action? BackgroundDrag;

    public void Tick(double dt)
    {
        _blinkClock += dt;
        if (!_animating && Advance(0))
            Animate();
        InvalidateVisual();
    }

    // Once a second, lit for 0.4 s, as filmed on a real unit.
    private bool BlinkOn => _blinkClock % 1.0 < 0.4;
    private double Now => _clock.Elapsed.TotalSeconds;
    private bool StripShown => _strip > 0.9;

    // ---- Animation ----

    // Frame-rate updates only run while something is moving, so an idle overlay doesn't keep WPF
    // rendering at the monitor's refresh rate alongside Run8.
    private void Animate()
    {
        if (_animating)
            return;
        _animating = true;
        _lastFrame = Now;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopAnimating()
    {
        _animating = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double now = Now;
        bool moving = Advance(Math.Min(now - _lastFrame, 0.1));
        _lastFrame = now;
        if (moving)
            InvalidateVisual();
        else
            StopAnimating();
    }

    /// <summary>Moves every animated value toward its target; true while anything is still moving.</summary>
    private bool Advance(double dt)
    {
        double now = Now;
        if (_panel.Auto != _lastAuto)
        {
            _autoKick = Math.Sign(IndexOf(_panel.Auto) - IndexOf(_lastAuto));
            _autoKickUntil = now + 0.18;
            _lastAuto = _panel.Auto;
        }

        bool moving = false;
        moving |= _auto.Show(AutoPose(now));
        moving |= _dir.Show(DirPose());
        moving |= _bell.Show(BellPose());
        moving |= _time.Show(TimePose());
        moving |= _ind.TurnToward(IndexOf(_panel.Ind), dt);
        moving |= _speed.TurnToward(IndexOf(_panel.Speed), dt);
        bool hover = _stripHover || now < _hoverUntil || _held?.OnStrip == true;
        moving |= Chase(ref _strip, hover ? 1 : 0, dt, 12, 0.005);
        // Timed targets change later without any input, so keep running until they expire.
        return moving || now < _autoKickUntil || now < _hoverUntil;
    }

    /// <summary>For <see cref="SkinPreview"/>: text shown instead of the controller's.</summary>
    internal string? DisplayOverride { get; set; }

    /// <summary>For <see cref="SkinPreview"/>: jumps every animation to where it is heading.</summary>
    internal void Settle(int autoHeld = 0, bool strip = false)
    {
        _autoHeld = autoHeld;
        _lastAuto = _panel.Auto;
        SnapAnimations();
        _strip = strip ? 1 : 0;
    }

    /// <summary>For <see cref="SkinPreview"/>: holds the AUTO paddle and advances the animations by <paramref name="dt"/>.</summary>
    internal void PreviewStep(int autoHeld, double dt)
    {
        _autoHeld = autoHeld;
        _lastAuto = _panel.Auto;
        Advance(dt);
    }

    private void SnapAnimations()
    {
        _auto.Show(AutoPose(Now));
        _dir.Show(DirPose());
        _bell.Show(BellPose());
        _time.Show(TimePose());
        _ind.Show(PhotoControl.Detent(IndexOf(_panel.Ind)));
        _speed.Show(PhotoControl.Detent(IndexOf(_panel.Speed)));
    }

    private static bool Chase(ref double value, double target, double dt, double rate, double settle = 0.05)
    {
        if (Math.Abs(target - value) < settle)
        {
            bool changed = value != target;
            value = target;
            return changed;
        }
        value += (target - value) * (1 - Math.Exp(-dt * rate));
        return true;
    }

    private static string Lean(int away) => away > 0 ? "away" : away < 0 ? "toward" : "centre";

    // AUTO is spring-loaded: it only leaves the centre while held or for a moment after each step.
    // Applying pushes it away from the operator, following the list up toward Charge; releasing
    // pulls it toward them.
    private string AutoPose(double now) => Lean(_autoHeld != 0 ? _autoHeld : now < _autoKickUntil ? _autoKick : 0);

    // Forward pushes the paddle away from the operator; reverse pulls it toward them.
    private string DirPose() => Lean(_panel.Dir switch
    {
        Direction.Fwd => 1,
        Direction.Rev => -1,
        _ => 0,
    });

    // Off is toward the operator, Bell in the centre and Bell/Horn away, in the order printed on
    // the face.
    private string BellPose() => Lean(_panel.BellHorn switch
    {
        BellHorn.BellHorn => 1,
        BellHorn.Off => -1,
        _ => 0,
    });

    // Time is away from the operator, Status toward them.
    private string TimePose() => Lean(_panel.TiltTimeout ? 1 : _panel.Status ? -1 : 0);

    // ---- Regions ----

    // Hold is for the left button; HoldDown, if given, is what holding the right button does.
    private sealed record Region(Rect Bounds, string Tip, Action? Click = null, Action<int>? Step = null,
        Action<bool>? Hold = null, string? Id = null, bool OnStrip = false, Action<bool>? HoldDown = null);

    private void BuildRegions()
    {
        var p = _panel;

        // Faceplate legends: clicking a position selects it directly.
        AddLampRows<IndBrake>(_skin.Lamps["ind"], v => p.Ind = v, "IND brake (brake override)");
        AddLampRows<AutoBrake>(_skin.Lamps["auto"], v => p.Auto = v, "AUTO train brake");
        AddLampRows<Direction>(_skin.Lamps["dir"], v => p.Dir = v, "Direction");
        AddLampRows<SpeedSelect>(_skin.Lamps["speed"], v => p.Speed = v, "Speed selector");

        var legends = _skin.Legends;
        _regions.Add(new Region(legends["bell_horn"].ToRect(), "Bell / Horn: hold for horn", Hold: p.SetHorn));
        _regions.Add(new Region(legends["bell"].ToRect(), "Bell on", Click: () => { p.BellHorn = BellHorn.Bell; p.Notify(); }));
        _regions.Add(new Region(legends["bell_off"].ToRect(), "Bell off", Click: () => { p.BellHorn = BellHorn.Off; p.Notify(); }));
        _regions.Add(new Region(legends["time"].ToRect(), "Time: hold about 2 s, stopped, for the tilt time-out beep", Hold: h => p.SetButton(nameof(OcuPanel.TiltTimeout), h)));
        _regions.Add(new Region(legends["status"].ToRect(), "Status: hold", Hold: h => p.SetButton(nameof(OcuPanel.Status), h)));
        _regions.Add(new Region(legends["brightness"].ToRect(), "Brightness: click to step the lamps and display Full, Medium, Low",
            Click: p.StepBrightness));

        // Physical controls.
        foreach (var corner in new[] { "rsc", "rsc_left" })
        {
            _regions.Add(new Region(ButtonRect(corner), "RSC (reset safety control)", Hold: h =>
            {
                _rscCorner = h ? corner : null;
                p.SetButton(nameof(OcuPanel.Rsc), h);
            }));
        }
        _regions.Add(new Region(LeverRect(_auto), "AUTO brake paddle: click to apply, right-click to release, or wheel",
            Step: d => p.StepAuto(d), Id: "auto"));
        _regions.Add(new Region(LeverRect(_dir), "DIR paddle: click for forward, right-click for reverse, or wheel",
            Step: d => p.StepDir(d)));
        _regions.Add(new Region(LeverRect(_bell),
            "Bell / Horn: click for bell, click again and hold for horn, right-click to step down",
            Step: p.StepBell, Hold: p.SetHorn, Id: "bell"));
        _regions.Add(new Region(LeverRect(_time),
            "Time / Status: click and hold up for the tilt time-out, right-click and hold down for status",
            Hold: h => p.SetButton(nameof(OcuPanel.TiltTimeout), h),
            HoldDown: h => p.SetButton(nameof(OcuPanel.Status), h)));
        _regions.Add(new Region(_ind.Bounds, "IND brake knob: click to apply, right-click to release, or wheel",
            Step: d => p.StepInd(d)));
        _regions.Add(new Region(_speed.Bounds, "SPEED knob: click for faster, right-click for slower, or wheel",
            Step: d => p.StepSpeed(d)));

        // Emulator strip.
        foreach (var (rect, tip, click, hold) in StripButtons())
            _regions.Add(new Region(rect, tip, Click: click, Hold: hold, OnStrip: true));
    }

    private void AddLampRows<T>(SkinLampGroup group, Action<T> select, string tip) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var pts = group.Points;
        double spacing = pts.Length > 1 ? Math.Abs(pts[^1][1] - pts[0][1]) / (pts.Length - 1) : 18;
        for (int i = 0; i < values.Length && i < pts.Length; i++)
        {
            var value = values[i];
            var rect = new Rect(pts[i][0] + group.Legend[0], pts[i][1] - spacing / 2, group.Legend[1] - group.Legend[0], spacing);
            string label = value is SpeedSelect s ? OcuInput.Label(s) : value.ToString();
            _regions.Add(new Region(rect, $"{tip}: {label}", Click: () => { select(value); _panel.Notify(); }));
        }
    }

    private Rect ButtonRect(string name)
    {
        var b = _skin.Buttons[name];
        return new Rect(b.Center[0] - b.Bezel, b.Center[1] - b.Bezel, b.Bezel * 2, b.Bezel * 2);
    }

    private static Rect LeverRect(PhotoControl control)
    {
        var r = control.Bounds;
        r.Inflate(3, 3);
        return r;
    }

    /// <summary>How far the strip is still tucked down behind the case.</summary>
    private double StripOffset => (1 - _strip) * StripHeight;

    private IEnumerable<(Rect Rect, string Tip, Action? Click, Action<bool>? Hold)> StripButtons()
    {
        var p = _panel;
        double y = StripTop + 6, h = StripRise - 12, x = StripLeft + 8;
        Rect Next(double w)
        {
            var r = new Rect(x, y, w, h);
            x += w + 6;
            return r;
        }
        yield return (Next(86), "OCU power", () => { p.PowerOn = !p.PowerOn; p.Notify(); }, null);
        yield return (Next(96), "Headlight bright/dim", () => { p.HeadlightBright = !p.HeadlightBright; p.Notify(); }, null);
        yield return (Next(62), "PITCH (on the back of the real unit): hold", null, h => p.SetButton(nameof(OcuPanel.Pitch), h));
        // TILT DISABLED for the v1.2 play-test build; restore with its label in DrawStrip.
        // yield return (Next(62), "Simulate tilting the OCU past 45 degrees", () => { p.Tilted = !p.Tilted; p.Notify(); }, null);
        yield return (Next(116), "Cab changeover: MANUAL leaves Run8 alone, BELTPACK takes control", () => ChangeoverClicked?.Invoke(), null);
    }

    // ---- Rendering ----

    protected override void OnRender(DrawingContext dc)
    {
        var c = Controller;
        bool powered = _panel.PowerOn && c is not null && c.Power != OcuPower.Off;
        bool lampTest = c?.LampTest == true;

        dc.PushTransform(new TranslateTransform(0, StripRise));

        // The strip slides up from behind the case, so it goes first; the knobs also go behind the
        // case so its edge overlaps their hubs, as on the real unit.
        if (_strip > 0.005)
        {
            dc.PushTransform(new TranslateTransform(0, StripOffset));
            DrawStrip(dc);
            dc.Pop();
        }
        _ind.Draw(dc);
        _speed.Draw(dc);
        DrawLayer(dc, _skin.Base);

        DrawButton(dc, "rsc", _panel.Rsc && _rscCorner != "rsc_left");
        DrawButton(dc, "rsc_left", _panel.Rsc && _rscCorner == "rsc_left");
        _auto.Draw(dc);
        _dir.Draw(dc);
        _bell.Draw(dc);
        _time.Draw(dc);
        // The strip rises where the antenna stands, so the antenna gives way to it.
        if (ShowAntenna && _strip < 0.995)
        {
            dc.PushOpacity(1 - _strip);
            Antenna.Draw(dc, _skin.Antenna.CenterPoint, _skin.Antenna.Radius);
            dc.Pop();
        }

        dc.PushOpacity(PanelLevel);
        DrawLamps(dc, "ind", IndexOf(_panel.Ind), powered, lampTest, c?.IndLedFlashing == true);
        DrawLamps(dc, "auto", IndexOf(_panel.Auto), powered, lampTest, c?.AutoLedFlashing == true);
        DrawLamps(dc, "dir", IndexOf(_panel.Dir), powered, lampTest, c?.DirLedFlashing == true);
        DrawLamps(dc, "speed", IndexOf(_panel.Speed), powered, lampTest, c?.SpeedLedFlashing == true);
        DrawIcon(dc, "headlight", powered && (lampTest || _panel.HeadlightBright));
        DrawIcon(dc, "battery", powered && lampTest);
        dc.Pop();
        DrawDisplay(dc, powered, lampTest, c);
        dc.Pop();
    }

    /// <summary>How strongly the lamps, icons and lit display dots show for the sun key's setting.</summary>
    private double PanelLevel => _panel.Brightness switch
    {
        PanelBrightness.Low => 0.6,
        PanelBrightness.Medium => 0.8,
        _ => 1,
    };

    private static void DrawLayer(DrawingContext dc, SkinLayer layer) => dc.DrawImage(layer.Image, layer.Bounds);

    private void DrawButton(DrawingContext dc, string name, bool pressed)
    {
        if (!pressed)
            return;
        var b = _skin.Buttons[name];
        dc.DrawEllipse(DomePressed, DomeRim, b.CenterPoint, b.Dome, b.DomeY > 0 ? b.DomeY : b.Dome);
    }

    private void DrawLamps(DrawingContext dc, string group, int selected, bool powered, bool lampTest, bool flashing)
    {
        var pts = _skin.Lamps[group].Points;
        double r = _skin.LampRadius;
        for (int i = 0; i < pts.Length; i++)
        {
            if (!Lit(powered, lampTest, i == selected, flashing))
                continue;
            var center = new Point(pts[i][0], pts[i][1]);
            var colour = LampColour(group, i);
            dc.DrawEllipse(colour.Glow, null, center, r * 3.4, r * 3.4);
            dc.DrawEllipse(colour.Core, null, center, r * 0.8, r * 0.8);
            if (LegendWord(group, i, colour.Body) is var (box, letters, spot))
            {
                dc.PushOpacityMask(letters);
                dc.DrawRectangle(spot, null, box);
                dc.Pop();
            }
        }
    }

    private readonly Dictionary<(string, int), (Rect, Brush, Brush)?> _legendWords = new();

    /// <summary>
    /// A lamp's printed word, a mask of its letters, and the spot of colour on them. The real unit
    /// has a small LED behind the middle of each word that only tints a character or two.
    /// </summary>
    private (Rect Box, Brush Letters, Brush Spot)? LegendWord(string group, int index, Color body)
    {
        if (_legendWords.TryGetValue((group, index), out var word))
            return word;
        var mask = _skin.LegendTextMask;
        if (mask is not null && _skin.LegendText.TryGetValue(group, out var boxes) && index < boxes.Length)
        {
            var box = boxes[index].ToRect();
            double k = mask.PixelWidth / _skin.Base.W;
            var pixels = new Int32Rect((int)(box.X * k), (int)(box.Y * k), (int)Math.Ceiling(box.Width * k), (int)Math.Ceiling(box.Height * k));
            // About one character's width either side of the centre.
            double radius = box.Height * 0.6;
            var spot = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0xE6, body.R, body.G, body.B), 0),
                new(Color.FromArgb(0x80, body.R, body.G, body.B), 0.5),
                new(Color.FromArgb(0, body.R, body.G, body.B), 1),
            })
            {
                RadiusX = Math.Min(radius / box.Width, 1),
                RadiusY = Math.Min(radius / box.Height, 1),
            };
            word = (box, Freeze(new ImageBrush(new CroppedBitmap(mask, pixels))), Freeze(spot));
        }
        _legendWords[(group, index)] = word;
        return word;
    }

    /// <summary>
    /// As lit on a real unit. SPEED: Stop red, Coast B and Coast yellow, Couple and up green. DIR: N
    /// red, Rev yellow, Fwd green. IND: Emerg red, Rel green, the rest yellow. AUTO: Charge red, Rel
    /// green, the rest yellow.
    /// </summary>
    private static LampBrushes LampColour(string group, int index) => group switch
    {
        "speed" => index == (int)SpeedSelect.Stop ? RedLamp
            : index <= (int)SpeedSelect.Coast ? YellowLamp
            : GreenLamp,
        "dir" => index == (int)Direction.Neutral ? RedLamp
            : index == (int)Direction.Rev ? YellowLamp
            : GreenLamp,
        "ind" => index == (int)IndBrake.Rel ? GreenLamp
            : index == (int)IndBrake.Emerg ? RedLamp
            : YellowLamp,
        "auto" => index == (int)AutoBrake.Rel ? GreenLamp
            : index == (int)AutoBrake.Charge ? RedLamp
            : YellowLamp,
        _ => RedLamp,
    };

    private sealed record LampBrushes(Brush Core, Brush Glow, Color Body);

    private static LampBrushes Lamp(Color centre, Color body, Color edge) => new(
        Freeze(new RadialGradientBrush(new GradientStopCollection { new(centre, 0), new(body, 0.55), new(edge, 1) })),
        Freeze(new RadialGradientBrush(Color.FromArgb(0x8C, body.R, body.G, body.B), Color.FromArgb(0, body.R, body.G, body.B))),
        body);

    private void DrawIcon(DrawingContext dc, string name, bool lit)
    {
        if (!lit)
            return;
        var icon = _skin.Icons[name];
        dc.DrawEllipse(IconGlow, null, icon.CenterPoint, icon.Radius * 1.5, icon.Radius * 1.2);
    }

    // Four separate 5x7 character modules behind the red filter, with a decimal point LED in the
    // gap after each one. Gaps are in dot pitches.
    private const int DisplayChars = 4;
    private const double ModuleGap = 1.8;
    private const double ScrollCharsPerSecond = 2.5;
    private static readonly string DisplayTestText = new(DotMatrixFont.AllDots, DisplayChars);

    private void DrawDisplay(DrawingContext dc, bool powered, bool lampTest, RclController? c)
    {
        var rect = _skin.Display.ToRect();
        dc.DrawRoundedRectangle(DisplayBg, null, rect, 2, 2);

        const int cols = DotMatrixFont.Width, rows = DotMatrixFont.Rows;
        // Modules and gaps, plus the last decimal point and its dot.
        double span = DisplayChars * cols + (DisplayChars - 0.5) * ModuleGap + 0.5;
        double pitch = Math.Min((rect.Width - 6) / span, (rect.Height - 8) / rows);
        double moduleStep = (cols + ModuleGap) * pitch;
        double x0 = rect.X + (rect.Width - span * pitch) / 2;
        double y0 = rect.Y + (rect.Height - rows * pitch) / 2;
        double dot = pitch * 0.36;
        double level = PanelLevel;

        string text = lampTest ? "" : c?.DisplayTest == true ? DisplayTestText : DisplayOverride ?? c?.DisplayText ?? "";
        var cells = powered ? DisplayFrame(text) : new (char Ch, bool Point)[DisplayChars];
        for (int m = 0; m < DisplayChars; m++)
        {
            double left = x0 + m * moduleStep;
            var face = new Rect(left - pitch * 0.25, y0 - pitch * 0.25, cols * pitch + pitch * 0.5, rows * pitch + pitch * 0.5);
            dc.DrawRoundedRectangle(ModuleFace, null, face, pitch * 0.3, pitch * 0.3);

            var glyph = DotMatrixFont.Cell(cells[m].Ch == '\0' ? ' ' : cells[m].Ch);
            for (int col = 0; col < cols; col++)
                for (int row = 0; row < rows; row++)
                    DrawDot(new Point(left + (col + 0.5) * pitch, y0 + (row + 0.5) * pitch), (glyph[col] >> row & 1) != 0);
            DrawDot(new Point(left + (cols + ModuleGap / 2) * pitch, y0 + (rows - 0.5) * pitch), cells[m].Point);
        }

        void DrawDot(Point center, bool on)
        {
            if (on)
            {
                dc.PushOpacity(level);
                dc.DrawEllipse(DotGlow, null, center, dot * 1.9, dot * 1.9);
                dc.DrawEllipse(DotOn, null, center, dot, dot);
                dc.Pop();
            }
            else
            {
                dc.DrawEllipse(powered ? DotOff : DotUnpowered, null, center, dot * 0.9, dot * 0.9);
            }
        }
    }

    /// <summary>
    /// The character in each module for the current frame. Text that fits sits in the modules, with
    /// a decimal point lighting the LED after its digit; longer text scrolls a character at a time.
    /// </summary>
    private (char Ch, bool Point)[] DisplayFrame(string text)
    {
        var frame = new (char Ch, bool Point)[DisplayChars];
        var cells = new List<(char Ch, bool Point)>();
        foreach (char ch in text)
        {
            if (ch == '.' && cells.Count > 0 && !cells[^1].Point)
                cells[^1] = (cells[^1].Ch, true);
            else
                cells.Add((ch, false));
        }

        if (cells.Count <= DisplayChars)
        {
            bool numeric = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            int first = numeric ? DisplayChars - cells.Count : (DisplayChars - cells.Count) / 2;
            for (int i = 0; i < cells.Count; i++)
                frame[first + i] = cells[i];
            return frame;
        }

        // Start on the first characters, run off to blank, then come back round.
        var tape = cells.Concat(Enumerable.Repeat((' ', false), DisplayChars)).ToList();
        int offset = (int)(_blinkClock * ScrollCharsPerSecond) % tape.Count;
        for (int i = 0; i < DisplayChars; i++)
            frame[i] = tape[(offset + i) % tape.Count];
        return frame;
    }

    private void DrawStrip(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(StripBrush, DarkPen, new Rect(StripLeft, StripTop, StripRight - StripLeft, StripHeight), 8, 8);
        var labels = new (string Text, Brush? Accent)[]
        {
            (_panel.PowerOn ? "POWER ON" : "POWER OFF", _panel.PowerOn ? AccentOn : null),
            (_panel.HeadlightBright ? "HEADLT BRT" : "HEADLT DIM", _panel.HeadlightBright ? AccentOn : null),
            ("PITCH", _panel.Pitch ? AccentOn : null),
            // (_panel.Tilted ? "TILTED" : "TILT", _panel.Tilted ? AccentWarn : null),
            (Beltpack ? "CAB: BELTPACK" : "CAB: MANUAL", Beltpack ? AccentWarn : null),
        };
        double right = 0, cy = 0;
        int i = 0;
        foreach (var (rect, _, _, _) in StripButtons())
        {
            var (text, accent) = labels[i++];
            dc.DrawRoundedRectangle(StripButton, new Pen(accent ?? Brushes.Gray, accent is null ? 1 : 2), rect, 5, 5);
            Text(dc, text, rect.X + rect.Width / 2, rect.Y + rect.Height / 2, 12, LegendBold, accent ?? TextBrush);
            right = rect.Right;
            cy = rect.Y + rect.Height / 2;
        }

        dc.DrawEllipse(LinkOk ? AccentOn : AccentWarn, null, new Point(right + 12, cy), 5, 5);
        var link = new FormattedText(LinkText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Legend, 11, TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = StripRight - 8 - right - 22,
            MaxLineCount = 2,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(link, new Point(right + 22, cy - link.Height / 2));
    }

    private bool Lit(bool powered, bool lampTest, bool selected, bool flashing)
    {
        if (!powered)
            return false;
        if (lampTest)
            return true;
        return selected && (!flashing || BlinkOn);
    }

    private void Text(DrawingContext dc, string text, double x, double y, double size, Typeface face, Brush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip) { TextAlignment = TextAlignment.Center };
        dc.DrawText(ft, new Point(x, y - ft.Height / 2));
    }

    private static int IndexOf<T>(T value) where T : struct, Enum => Array.IndexOf(Enum.GetValues<T>(), value);

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    // ---- Mouse ----

    private Region? HitRegion(Point p)
    {
        for (int i = _regions.Count - 1; i >= 0; i--)
        {
            var r = _regions[i];
            if (r.Bounds.Contains(p) && (!r.OnStrip || StripShown))
                return r;
        }
        return null;
    }

    /// <summary>The mouse position in skin units.</summary>
    private Point SkinPosition(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Point(p.X, p.Y - StripRise);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = SkinPosition(e);
        bool onStrip = _strip > 0.05 && pos.Y < StripVisibleBottom && pos.X >= StripLeft && pos.X <= StripRight;
        bool onAntenna = ShowAntenna && Antenna.Bounds(_skin.Antenna.CenterPoint, _skin.Antenna.Radius).Contains(pos);
        SetStripHover(StripTrigger.Contains(pos) || onAntenna || onStrip);
        var region = HitRegion(pos);
        Cursor = region is null ? Cursors.SizeAll : Cursors.Hand;
        // TOOLTIPS OFF: the face is labelled and a labelled picture ships with the docs; uncomment to
        // bring back the hover names.
        // ToolTip = region?.Tip;
    }

    protected override void OnMouseLeave(MouseEventArgs e) => SetStripHover(false);

    private void SetStripHover(bool hover)
    {
        if (hover == _stripHover)
            return;
        _stripHover = hover;
        if (!hover)
            _hoverUntil = Now + StripLingerSeconds;
        Animate();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var region = HitRegion(SkinPosition(e));
        if (region is null)
        {
            BackgroundDrag?.Invoke();
            return;
        }
        e.Handled = true;
        // Bell/Horn steps up from Off to Bell; from Bell the top position is momentary, so hold it.
        bool horn = region.Id == "bell" && _panel.BellHorn == BellHorn.Bell;
        if (region.Hold is not null && (region.Step is null || horn))
        {
            Grab(region, MouseButton.Left);
            region.Hold(true);
        }
        else if (region.Step is not null)
        {
            StepPress(region, 1, MouseButton.Left);
        }
        else
        {
            region.Click?.Invoke();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (Release(MouseButton.Left))
            e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        var region = HitRegion(SkinPosition(e));
        if (region?.HoldDown is not null)
        {
            Grab(region, MouseButton.Right);
            region.HoldDown(true);
            e.Handled = true;
        }
        else if (region?.Step is not null)
        {
            StepPress(region, -1, MouseButton.Right);
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        if (Release(MouseButton.Right) || HitRegion(SkinPosition(e))?.Step is not null)
            e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;
        var region = HitRegion(SkinPosition(e));
        if (region?.Step is not null)
        {
            region.Step(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    /// <summary>A step from the mouse; the AUTO paddle stays deflected until the button is let go.</summary>
    private void StepPress(Region region, int delta, MouseButton button)
    {
        region.Step!(delta);
        if (region.Id == "auto")
        {
            _autoHeld = delta;
            Grab(region, button);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_held is not null)
            Release(_heldButton);
    }

    private void Grab(Region region, MouseButton button)
    {
        _held = region;
        _heldButton = button;
        CaptureMouse();
    }

    private bool Release(MouseButton button)
    {
        if (_held is null || _heldButton != button)
            return false;
        (button == MouseButton.Right && _held.HoldDown is not null ? _held.HoldDown : _held.Hold)?.Invoke(false);
        if (_held.Id == "auto")
            _autoHeld = 0;
        _held = null;
        ReleaseMouseCapture();
        return true;
    }
}
