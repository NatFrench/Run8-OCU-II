using Beltpack.Core.Ocu;

namespace Beltpack.Core.Rcl;

/// <summary>
/// Dynamic speed control: the selector picks a target speed and this decides throttle notch and
/// locomotive (independent) brake. Power is used below the target and the locomotive brake
/// above it, never both.
/// </summary>
public sealed class SpeedRegulator
{
    // Run8's acceleration reading lags and tops out below what a light engine can do, so the
    // acceleration is also worked out from the speed over this window.
    private const double AccelWindowSeconds = 1.0;
    private const double AccelDisagreeMphPerSecond = 0.15;
    private const double FineSpeedMph = 12.5;

    private readonly RclSettings _s;
    private readonly IndependentBrakeRegulator _ind;
    private readonly Queue<(double Time, double Mph)> _history = new();
    private double _clock;
    private int _notch;
    private double _sinceStep;
    private double? _stopIndy;
    private bool _stopHolding;
    private bool _holding;
    private int _brakeSteps;
    private double _sinceBrakeStep;
    private double _sincePower;

    public SpeedRegulator(RclSettings settings, IndependentBrakeRegulator ind)
    {
        _s = settings;
        _ind = ind;
    }

    public int Notch => _notch;

    /// <summary>Locomotive brake cylinder pressure asked for by the last update, outside Stop.</summary>
    public double BrakePsi { get; private set; }

    public void Reset()
    {
        _notch = 0;
        _sinceStep = 0;
        _stopIndy = null;
        _stopHolding = false;
        _holding = false;
        _brakeSteps = 0;
        BrakePsi = 0;
        _history.Clear();
    }

    public (int Notch, byte Indy) Update(double dt, SpeedSelect select, double speedMph, double accelMphPerMin, int notchCap,
        int brakeCylinderPsi = 0)
    {
        _clock += dt;
        _sinceStep += dt;
        _sinceBrakeStep += dt;
        _sincePower += dt;

        // Stacking notches against brakes that are still bleeding off gives a surge once they let
        // go, so the first notch's effect is only judged once they're off.
        if (brakeCylinderPsi > _s.ReleasingBrakeCylinderPsi)
        {
            notchCap = Math.Min(notchCap, 1);
            _sinceStep = 0;
        }
        double accel = Acceleration(speedMph, accelMphPerMin / 60.0);

        if (select != SpeedSelect.Stop)
        {
            _stopIndy = null;
            _stopHolding = false;
        }

        switch (select)
        {
            case SpeedSelect.Stop:
                _notch = 0;
                _brakeSteps = 0;
                BrakePsi = 0;
                return (0, StopBrake(dt, speedMph, accel));

            case SpeedSelect.Coast:
            case SpeedSelect.CoastB:
                _notch = 0;
                WorkBrake(_s.CoastLimitMph, speedMph, accel);
                return (0, BrakeLever(select == SpeedSelect.CoastB ? _s.CoastBPsi : 0));
        }

        double target = _s.TargetMph(select);
        double error = target - speedMph;

        if ((_brakeSteps > 0 || error <= 0) && WorkBrake(target, speedMph, accel))
        {
            _notch = 0;
            _sinceStep = 0;
        }
        else if (error <= 0 || notchCap == 0)
        {
            _notch = 0;
            _sinceStep = 0;
            if (error <= 0)
                _holding = true;
        }
        else
        {
            // Like an engineer loading up: one notch, wait to see what it does, another only if
            // the train is picking up well short of the ramp rate, one off only if it's picking
            // up far too fast. Power comes off as the train arrives at the target, and isn't put
            // back while it stays close to it.
            bool arriving = accel > 0 && speedMph + accel * _s.ArrivalSeconds >= target;
            if (arriving)
                _holding = true;
            else if (error > _s.HoldBandMph)
                _holding = false;

            if (arriving)
            {
                if (_notch > 0 && _sinceStep >= _s.ArrivalStepDownSeconds)
                    Step(-1);
            }
            else if (_holding)
            {
                if (accel < -_s.HoldDropMphPerSecond && _sinceStep >= _s.ThrottleStepUpSeconds)
                    Step(1);
            }
            else if (accel < _s.RampTooSlowMphPerSecond && (_notch == 0 || _sinceStep >= _s.ThrottleStepUpSeconds))
            {
                Step(1);
            }
            else if (accel > _s.RampTooFastMphPerSecond && _notch > 1 && _sinceStep >= _s.ThrottleStepDownSeconds)
            {
                Step(-1);
            }
        }
        _notch = Math.Clamp(_notch, 0, notchCap);
        if (_notch > 0)
            _sincePower = 0;
        return (_notch, BrakeLever(0));
    }

    /// <summary>
    /// Like the throttle but the other way: one step of locomotive brake, wait to see what it does,
    /// another only if the train still isn't slowing, one off if it's slowing far too fast, and off
    /// a step at a time as it gets back down to the target. Returns whether any brake is on.
    /// </summary>
    private bool WorkBrake(double target, double speedMph, double accel)
    {
        double over = speedMph - target;
        if (_brakeSteps == 0)
        {
            // Still picking up with the power off for a while means a grade.
            bool overspeed = over > _s.OverspeedDeadbandMph && accel > -_s.BrakeTooSlowMphPerSecond;
            bool runningAway = over >= 0 && accel > _s.HoldDropMphPerSecond && _sincePower >= _s.BrakeStepUpSeconds;
            if (overspeed || runningAway)
                BrakeStep(1);
            return _brakeSteps > 0;
        }

        _holding = true;
        bool arriving = accel < -_s.BrakeTooSlowMphPerSecond && speedMph + accel * _s.ArrivalSeconds <= target;
        if (over < -_s.HoldBandMph)
        {
            if (_sinceBrakeStep >= _s.ArrivalStepDownSeconds)
                BrakeStep(-1);
        }
        else if (arriving)
        {
            if (_sinceBrakeStep >= _s.BrakeArrivalStepSeconds)
                BrakeStep(-1);
        }
        else if (over > _s.HoldBandMph)
        {
            if (accel > -_s.BrakeTooSlowMphPerSecond && _sinceBrakeStep >= _s.BrakeStepUpSeconds)
                BrakeStep(1);
            else if (accel < -_s.BrakeTooFastMphPerSecond && _brakeSteps > 1 && _sinceBrakeStep >= _s.BrakeStepDownSeconds)
                BrakeStep(-1);
        }
        // Holding: the acceleration worked out from tenths of a mph is too noisy to trim on, so
        // it's left to drift a little either side before a step goes on or comes off.
        else if (accel > _s.HoldDropMphPerSecond && over > _s.HoldBandMph / 2 && _sinceBrakeStep >= _s.BrakeStepUpSeconds)
        {
            BrakeStep(1);
        }
        else if (accel < -_s.HoldDropMphPerSecond && over < -_s.HoldBandMph / 2 && _sinceBrakeStep >= _s.BrakeStepDownSeconds)
        {
            BrakeStep(-1);
        }
        return _brakeSteps > 0;
    }

    private void BrakeStep(int by)
    {
        int full = (int)Math.Ceiling(_s.IndyFullScalePsi / _s.BrakeStepPsi);
        _brakeSteps = Math.Clamp(_brakeSteps + by, 0, full);
        _sinceBrakeStep = 0;
    }

    private byte BrakeLever(double basePsi)
    {
        BrakePsi = Math.Min(basePsi + _brakeSteps * _s.BrakeStepPsi, _s.IndyFullScalePsi);
        return _ind.LeverFor(BrakePsi);
    }

    private void Step(int by)
    {
        _notch += by;
        _sinceStep = 0;
    }

    private double Acceleration(double speedMph, double reported)
    {
        _history.Enqueue((_clock, speedMph));
        while (_history.Count > 1 && _clock - _history.Peek().Time > AccelWindowSeconds)
            _history.Dequeue();
        if (_clock - _history.Peek().Time < AccelWindowSeconds / 2)
            return reported;

        // Speed only comes in tenths, so a line is fitted through the window rather than taking
        // two readings. Above FineSpeedMph it comes in whole mph, too coarse to use unless it
        // clearly disagrees with Run8's figure.
        double meanT = 0, meanV = 0;
        foreach (var (t, v) in _history) { meanT += t; meanV += v; }
        meanT /= _history.Count;
        meanV /= _history.Count;
        double num = 0, den = 0;
        foreach (var (t, v) in _history) { num += (t - meanT) * (v - meanV); den += (t - meanT) * (t - meanT); }
        double measured = den > 0 ? num / den : reported;
        if (speedMph < FineSpeedMph)
            return measured;
        return Math.Abs(measured) > Math.Abs(reported) + AccelDisagreeMphPerSecond ? measured : reported;
    }

    /// <summary>
    /// Works the locomotive brake to slow at a steady rate, then applies it fully once at rest and
    /// keeps it there for as long as Stop is selected.
    /// </summary>
    private byte StopBrake(double dt, double speedMph, double accelMphPerSecond)
    {
        if (_stopHolding || speedMph <= _s.StopHoldMph)
        {
            _stopHolding = true;
            return _s.StopIndy;
        }
        double decel = -accelMphPerSecond;
        double indy = _stopIndy ?? _s.StopStartIndy;
        indy += (_s.StopDecelMphPerSecond - decel) * _s.StopIndyPerMphPerSecond * dt;
        _stopIndy = Math.Clamp(indy, 0, _s.StopIndy);
        return (byte)_stopIndy.Value;
    }
}
