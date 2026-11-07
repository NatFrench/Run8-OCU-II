using Beltpack.Core.Rcl;
using Beltpack.Core.Run8;

namespace Beltpack.Core.Tests;

/// <summary>
/// Crude stand-in for a Run8 light engine, enough to exercise the controller's closed loops.
/// </summary>
internal sealed class FakeLoco
{
    public double Speed;
    public double BrakePipe = 90;
    public double Equalizing = 90;
    public double MainReservoir = 140;
    public int FeedValve = 90;
    public double Grade;
    public bool PcsOpen;
    public double PcsResetSeconds = 120;
    public int UndesiredEmergencies;
    public double BrakeCylinder = 72;
    public double TractionPerNotch = 0.3;
    public double BrakingPerPsi = 0.06;
    public double ReportedAccelLagSeconds;
    public double TractionLagSeconds = 1.0;
    // Run8 V3's independent lever gives less than its share of the full pressure low down: about
    // 14 psi at 72/255 where linear would be 20. An exponent of 1.3 matches.
    public double IndyCurve = 1.0;
    private double _reportedAccel;
    private double _traction;
    private double _pcsTimer;
    private double _udeTimer;
    private double _accel;
    private double _airflow;

    public void Step(double dt, LocoCommand c)
    {
        // Handle-to-reduction map measured in Run8 V3.
        double targetBp = c.TrainBrake == 0 ? FeedValve
            : c.TrainBrake >= 255 ? 0
            : FeedValve - Math.Clamp(7 + (c.TrainBrake - 26) * 19.0 / 224, 6, 26);

        // Run8 dumps a light engine into emergency when the equalizing reservoir runs well ahead
        // of the brake pipe, as happens when the handle jumps straight to full service.
        _udeTimer = BrakePipe - targetBp > 15 && targetBp > 0 ? _udeTimer + dt : 0;
        if (_udeTimer >= 1.5 && !PcsOpen)
        {
            PcsOpen = true;
            UndesiredEmergencies++;
        }

        // Run8: an emergency opens the PCS, which keeps the brake pipe vented until the reverser has
        // been centred with the train brake fully applied for two minutes, then released.
        if (c.TrainBrake >= 255 && BrakePipe < 30)
            PcsOpen = true;
        if (PcsOpen)
        {
            if (c.TrainBrake == 0 && _pcsTimer >= PcsResetSeconds)
                PcsOpen = false;
            else if (c.Reverser == ReverserState.Neutral && c.TrainBrake >= 140)
                _pcsTimer += dt;
            else
                _pcsTimer = 0;
        }
        if (!PcsOpen)
            _pcsTimer = 0;
        else
            targetBp = 0;
        double before = BrakePipe;
        double rate = targetBp > BrakePipe ? 4 : c.TrainBrake >= 255 ? 60 : 6;
        BrakePipe += Math.Clamp(targetBp - BrakePipe, -rate * dt, rate * dt);
        Equalizing = targetBp;
        _airflow = Math.Abs(BrakePipe - before) / dt * 20;

        // Lags and magnitudes roughly match Run8 telemetry from a two-unit light engine: traction
        // follows the throttle over about a second, and the brake cylinder builds at ~20 psi/s and
        // bleeds off at ~12 psi/s.
        double tractionTarget = c.Reverser == ReverserState.Neutral ? 0 : c.Throttle * TractionPerNotch;
        _traction += (tractionTarget - _traction) * Math.Min(1, dt / TractionLagSeconds);

        double trainBc = BrakePipe < 10 ? 72 : Math.Clamp((FeedValve - BrakePipe) * 2.5, 0, 64);
        double bcTarget = Math.Max(Math.Pow(c.Indy / 255.0, IndyCurve) * 72, trainBc);
        double bcRate = bcTarget > BrakeCylinder ? 20 : 12;
        BrakeCylinder += Math.Clamp(bcTarget - BrakeCylinder, -bcRate * dt, bcRate * dt);

        double resist = Speed > 0 ? 0.02 : 0;
        double net = _traction - Grade;
        double braking = BrakeCylinder * BrakingPerPsi + resist;

        if (Speed <= 0 && Math.Abs(net) <= braking)
        {
            _accel = 0;
            Speed = 0;
        }
        else
        {
            _accel = net - braking;
            Speed = Math.Max(0, Speed + _accel * dt);
        }
        _reportedAccel = ReportedAccelLagSeconds > 0
            ? _reportedAccel + (_accel - _reportedAccel) * Math.Min(1, dt / ReportedAccelLagSeconds)
            : _accel;
    }

    public Run8Telemetry Telemetry() => new()
    {
        SpeedMph = Math.Round(Speed, 1),
        MainReservoirPsi = (int)MainReservoir,
        EqualizingReservoirPsi = (int)Math.Round(Equalizing),
        BrakePipePsi = (int)Math.Round(BrakePipe),
        BrakeCylinderPsi = (int)Math.Round(BrakeCylinder),
        AirflowCfm = (int)Math.Min(255, _airflow),
        FeedValvePsi = FeedValve,
        // Run8 sends acceleration in a single byte spanning +/-100 mph/min.
        AccelerationMphPerMin = Math.Clamp(_reportedAccel * 60, -100, 100),
        LocoStatus1 = PcsOpen ? LocoStatus1.PcsOpen : 0,
        HasEot = false,
    };
}
