using Beltpack.Core.Run8;

namespace Beltpack.Core.Rcl;

public enum TrainBrakeDemand { Release, Min, Light, Med, Full, Emergency }

/// <summary>
/// Converts a requested brake pipe reduction into a Run8 automatic brake handle value.
/// Applications only ever deepen: freight cars have direct release, so backing the handle
/// off part-way would release the whole train.
/// </summary>
public sealed class TrainBrakeRegulator
{
    private const byte Emergency = 255;
    private readonly RclSettings _s;
    private double _handle;
    private byte _target;
    private double _appliedPsi;
    private double _sinceTrim;
    private int? _lastEr;

    public TrainBrakeRegulator(RclSettings settings) => _s = settings;

    public byte Handle => (byte)Math.Round(_handle);
    public double AppliedReductionPsi => _appliedPsi;

    public byte Update(double dt, TrainBrakeDemand demand, Run8Telemetry? telemetry)
    {
        if (demand == TrainBrakeDemand.Emergency || _target == Emergency)
        {
            if (demand == TrainBrakeDemand.Release)
                return Release();
            _handle = _target = Emergency;
            return Emergency;
        }

        if (demand == TrainBrakeDemand.Release)
            return Release();

        double psi = ReductionPsi(demand);
        if (psi > _appliedPsi)
        {
            _appliedPsi = psi;
            _target = Math.Max(_target, HandleFor(psi));
            _handle = Math.Max(_handle, _s.TrainBrakeMinReduction);
            _sinceTrim = 0;
            _lastEr = null;
        }

        // Run8 turns a sudden large reduction on a short consist into an undesired emergency,
        // so the handle is walked toward its target instead of jumping there.
        _handle = Math.Min(_target, _handle + _s.TrainBrakeApplyRate * dt);

        _sinceTrim += dt;
        if (_s.TrimTrainBrakeFromTelemetry && telemetry is not null && _sinceTrim >= 1.0 && _handle >= _target)
        {
            _sinceTrim = 0;
            int er = telemetry.EqualizingReservoirPsi;
            bool settled = _lastEr == er;
            double desiredEr = telemetry.FeedValvePsi - _appliedPsi;
            if (settled && er > desiredEr + 1 && _target < _s.TrainBrakeFullService)
                _target = (byte)Math.Min(_target + 3, _s.TrainBrakeFullService);
            _lastEr = er;
        }

        return Handle;
    }

    private byte Release()
    {
        _handle = 0;
        _target = 0;
        _appliedPsi = 0;
        _lastEr = null;
        return 0;
    }

    private double ReductionPsi(TrainBrakeDemand d) => d switch
    {
        TrainBrakeDemand.Min => _s.MinReductionPsi,
        TrainBrakeDemand.Light => _s.LightReductionPsi,
        TrainBrakeDemand.Med => _s.MedReductionPsi,
        TrainBrakeDemand.Full => _s.FullReductionPsi,
        _ => 0,
    };

    private byte HandleFor(double psi)
    {
        double span = _s.FullReductionPsi - _s.MinReductionPsi;
        double fraction = span <= 0 ? 1 : Math.Clamp((psi - _s.MinReductionPsi) / span, 0, 1);
        return (byte)Math.Round(_s.TrainBrakeMinReduction + fraction * (_s.TrainBrakeFullService - _s.TrainBrakeMinReduction));
    }
}
