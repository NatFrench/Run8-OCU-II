using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Beltpack.Overlay;

/// <summary>
/// A paddle, toggle or knob drawn from its photo in each position (see tools/make_skin.py). A change
/// of position swaps straight to the new photo, as the real part snaps over; a knob clicks through
/// each detent on the way to a distant one.
/// </summary>
internal sealed class PhotoControl
{
    private const double DetentSeconds = 0.05;

    private readonly SkinControl _skin;
    private string _shown;
    private double _sinceDetent = DetentSeconds;

    public PhotoControl(SkinControl skin, string pose)
    {
        _skin = skin;
        _shown = pose;
    }

    /// <summary>Everything the control covers in any position.</summary>
    public Rect Bounds => _skin.Hit.ToRect();

    public static string Detent(int index) => index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Shows <paramref name="pose"/>; true if that changed the picture.</summary>
    public bool Show(string pose)
    {
        if (pose == _shown)
            return false;
        _shown = pose;
        return true;
    }

    /// <summary>For a knob: moves a detent at a time toward <paramref name="detent"/>; true while still turning.</summary>
    public bool TurnToward(int detent, double dt)
    {
        _sinceDetent += dt;
        int shown = int.Parse(_shown, CultureInfo.InvariantCulture);
        if (shown != detent && _sinceDetent >= DetentSeconds)
        {
            Show(Detent(shown + Math.Sign(detent - shown)));
            _sinceDetent = 0;
            return true;
        }
        return shown != detent;
    }

    public void Draw(DrawingContext dc)
    {
        var layer = _skin.Poses[_shown];
        dc.DrawImage(layer.Image, layer.Bounds);
    }
}
