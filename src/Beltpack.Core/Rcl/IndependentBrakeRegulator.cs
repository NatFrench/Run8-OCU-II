using Beltpack.Core.Run8;

namespace Beltpack.Core.Rcl;

/// <summary>
/// Converts a locomotive (independent) brake cylinder pressure into a Run8 independent brake lever
/// value. The lever is taken as linear up to <see cref="RclSettings.IndyFullScalePsi"/>, then each
/// pressure is trimmed from the brake cylinder reading, because locomotives differ.
/// </summary>
public sealed class IndependentBrakeRegulator
{
    private const double MaxTrim = 100;
    private readonly RclSettings _s;
    private readonly Dictionary<double, double> _trim = new();
    private double _heldPsi;
    private double _sinceTrim;
    private int? _lastBc;

    public IndependentBrakeRegulator(RclSettings settings) => _s = settings;

    public byte LeverFor(double psi)
    {
        if (psi <= 0)
            return 0;
        double lever = psi / _s.IndyFullScalePsi * 255 + _trim.GetValueOrDefault(psi);
        return (byte)Math.Clamp(Math.Round(lever), 1, 255);
    }

    /// <summary>
    /// Call every tick with the pressure the lever alone is holding, or zero when something else
    /// (the train brake, speed control, a penalty) is also acting on the brake cylinder.
    /// </summary>
    public void Trim(double dt, double heldPsi, Run8Telemetry? telemetry)
    {
        if (!_s.TrimIndyFromTelemetry || telemetry is null || heldPsi <= 0 || heldPsi != _heldPsi)
        {
            _heldPsi = heldPsi;
            _sinceTrim = 0;
            _lastBc = null;
            return;
        }

        _sinceTrim += dt;
        if (_sinceTrim < 1.0)
            return;
        _sinceTrim = 0;

        // Corrects part of the error each time, so it settles without hunting even when the
        // locomotive's full-scale pressure is well off IndyFullScalePsi.
        int bc = telemetry.BrakeCylinderPsi;
        if (_lastBc == bc && Math.Abs(bc - heldPsi) > 1.5)
        {
            double trim = _trim.GetValueOrDefault(heldPsi) + (heldPsi - bc) * 255 / _s.IndyFullScalePsi * 0.6;
            _trim[heldPsi] = Math.Clamp(trim, -MaxTrim, MaxTrim);
        }
        _lastBc = bc;
    }
}
