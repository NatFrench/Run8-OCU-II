using Beltpack.Core.Ocu;
using Beltpack.Core.Run8;

namespace Beltpack.Core.Rcl;

public enum OcuPower { Off, SelfTest, Running }

/// <summary>Penalty brake applications, ordered by severity (CANAC job aid section 5.0).</summary>
public enum PenaltyKind { None, Service, TrainBrake, Emergency }

public enum AlarmPattern { None, Continuous }

/// <summary>
/// A real OCU II only beeps for the tilt time-out and sounds its alarm for RSC and tilt; it stays
/// silent for penalties, link problems and everything else.
/// </summary>
public enum OcuSound { ShortBeep }

public enum ReverserState { Rev, Neutral, Fwd }

public sealed record LocoCommand
{
    public int Throttle { get; init; }
    public byte Indy { get; init; }
    public byte TrainBrake { get; init; }
    public ReverserState Reverser { get; init; } = ReverserState.Neutral;
    public bool Bell { get; init; }
    public bool Horn { get; init; }
    public bool Sander { get; init; }
    public byte HeadlightFront { get; init; } = 1;
    public byte HeadlightRear { get; init; } = 1;
    public bool Alerter { get; init; }
}

/// <summary>
/// The emulated locomotive-side computer plus OCU feedback. Call <see cref="Update"/> on a fixed
/// tick with the current OCU controls and the latest Run8 telemetry; read <see cref="Command"/>
/// and the feedback properties afterwards.
/// </summary>
public sealed class RclController
{
    private readonly RclSettings _s;
    private readonly IndependentBrakeRegulator _ind;
    private readonly SpeedRegulator _speed;
    private readonly TrainBrakeRegulator _trainBrake;

    private OcuInput _prev = new();
    private double _clock;
    private double _selfTestElapsed;
    private double _speedSelectUntil = double.NegativeInfinity;
    private double _recoveryArmedUntil = double.NegativeInfinity;
    private double _rscElapsed;
    private double _rscHeld;
    private double _dragElapsed;
    private string? _draggingText;
    private double _stallElapsed;
    private bool _stallWarned;
    private double _slipElapsed;
    private bool _slipAnnounced;
    private bool _chargeAnnounced;
    private bool _wasCommandingMove;
    private double _autoBellUntil = double.NegativeInfinity;
    private PenaltyKind _recoveringFrom;
    // TILT DISABLED for the v1.2 play-test build (see Run).
    // private double _tiltElapsed;
    // private double _tiltBypassUntil = double.NegativeInfinity;
    private double _timeHeld;
    private bool _timeAccepted;
    private double _penaltyAt;
    private double _slipUntil = double.NegativeInfinity;
    private double _messageUntil = double.NegativeInfinity;
    private string _message = "";
    private bool _alerterPulse;
    private bool _linkWasOk = true;
    private bool _pcsWasOpen;
    private double _pcsResetElapsed;
    private bool _pcsReadyAnnounced;

    public RclController(RclSettings settings)
    {
        _s = settings;
        _ind = new IndependentBrakeRegulator(settings);
        _speed = new SpeedRegulator(settings, _ind);
        _trainBrake = new TrainBrakeRegulator(settings);
    }

    public event Action<OcuSound>? Sound;
    public event Action<string>? Talker;

    public OcuPower Power { get; private set; }
    public PenaltyKind Penalty { get; private set; }
    public string PenaltyReason { get; private set; } = "";
    public bool BrakeRecoveryPending { get; private set; }
    public bool MotionEnabled { get; private set; }
    public ReverserState ActiveReverser { get; private set; } = ReverserState.Neutral;
    public AlarmPattern Alarm { get; private set; }
    public string DisplayText { get; private set; } = "";

    /// <summary>The unit number of the locomotive in Run8, when known; otherwise the configured one is used.</summary>
    public string? ControllingUnit { get; set; }

    private string LocomotiveId => string.IsNullOrEmpty(ControllingUnit) ? _s.LocomotiveId : ControllingUnit;

    // Power-up as filmed on a real unit: every lamp, display dark; then every display dot, lamps off.
    private const double LampTestSeconds = 1.4;
    private const double DisplayTestSeconds = 1.2;

    /// <summary>Every lamp and icon lit, display dark.</summary>
    public bool LampTest { get; private set; }
    /// <summary>Every dot of the display lit, straight after the lamp test.</summary>
    public bool DisplayTest { get; private set; }
    public bool SpeedLedFlashing { get; private set; }
    public bool AutoLedFlashing { get; private set; }
    public bool IndLedFlashing { get; private set; }
    public bool DirLedFlashing { get; private set; }

    /// <summary>Seconds until Run8 will reset the open PCS; zero when not waiting on it.</summary>
    public double PcsResetRemaining { get; private set; }

    public LocoCommand Command { get; private set; } = new();

    public void Update(double dt, OcuInput input, Run8Telemetry? telemetry, bool linkOk)
    {
        _clock += dt;
        var prev = _prev;
        _prev = input;

        if (!input.PowerOn)
        {
            if (Power != OcuPower.Off)
                PowerDown();
            Command = SafeCommand();
            return;
        }

        if (Power == OcuPower.Off)
        {
            Power = OcuPower.SelfTest;
            _selfTestElapsed = 0;
        }

        if (Power == OcuPower.SelfTest)
        {
            _selfTestElapsed += dt;
            LampTest = _selfTestElapsed < LampTestSeconds;
            DisplayTest = !LampTest && _selfTestElapsed < LampTestSeconds + DisplayTestSeconds;
            // The real unit flashes the train brake lamp from the end of the lamp test, ahead of the
            // power-up penalty.
            AutoLedFlashing = !LampTest;
            DisplayText = "TEST";
            Command = SafeCommand();
            if (_selfTestElapsed >= _s.SelfTestSeconds)
            {
                Power = OcuPower.Running;
                LampTest = DisplayTest = false;
                TriggerPenalty(PenaltyKind.TrainBrake, "System reset");
                ShowMessage($"OK {LocomotiveId} {_s.OcuLetter} {_s.Channel}", 6);
            }
            return;
        }

        Run(dt, input, prev, telemetry, linkOk);
    }

    private void Run(double dt, OcuInput input, OcuInput prev, Run8Telemetry? t, bool linkOk)
    {
        double speed = t?.SpeedMph ?? 0;
        bool stopped = speed < 0.2;
        bool rscPressed = input.Rsc && !prev.Rsc;
        bool statusPressed = input.Status && !prev.Status;
        bool timePressed = input.TiltTimeout && !prev.TiltTimeout;
        bool pitchPressed = input.Pitch && !prev.Pitch;
        bool controlChanged = input.Ind != prev.Ind || input.Auto != prev.Auto || input.Dir != prev.Dir
            || input.Speed != prev.Speed || input.BellHorn != prev.BellHorn
            || rscPressed || statusPressed || timePressed || pitchPressed;

        if (rscPressed)
            _speedSelectUntil = _clock + _s.SpeedSelectWindowSeconds;
        _rscHeld = input.Rsc ? _rscHeld + dt : 0;

        // Communication with the sim.
        if (!linkOk && _linkWasOk)
        {
            if (MotionEnabled)
                TriggerPenalty(PenaltyKind.Service, "Communication interrupted");
        }
        _linkWasOk = linkOk;

        // Reset Safety Control (vigilance). Runs off STOP even when standing: the job aid's RSC test
        // is done in COAST with the reverser in neutral.
        bool rscActive = speed > 0.5 || input.Speed != SpeedSelect.Stop;
        bool otherControlResets = _s.AnyControlResetsRsc && controlChanged && _rscElapsed < _s.RscTimeoutSeconds;
        if (rscPressed || otherControlResets || !rscActive || Penalty != PenaltyKind.None)
            _rscElapsed = 0;
        else
            _rscElapsed += dt;
        bool rscWarning = _rscElapsed >= _s.RscTimeoutSeconds;
        if (_rscElapsed >= _s.RscTimeoutSeconds + _s.RscWarningSeconds)
        {
            _rscElapsed = 0;
            TriggerPenalty(PenaltyKind.TrainBrake, "Reset safety control timeout");
        }

        // The tilt time-out: Time is held, at a standstill, until the beep. It works without a link
        // to the sim, as on a real unit.
        _timeHeld = input.TiltTimeout ? _timeHeld + dt : 0;
        if (!input.TiltTimeout)
            _timeAccepted = false;
        else if (!_timeAccepted && stopped && _timeHeld >= _s.TiltTimeoutHoldSeconds)
        {
            _timeAccepted = true;
            // _tiltBypassUntil = _clock + _s.TiltBypassSeconds;
            Sound?.Invoke(OcuSound.ShortBeep);
            ShowMessage("TIME", 2);
            Talker?.Invoke($"Tilt timeout from {_s.OcuLetter}.");
        }

        // TILT DISABLED for the v1.2 play-test build. Uncomment this block, the tilt fields, the
        // bypass line above and the reset in PowerDown to restore tilt protection.
        bool tiltAlarm = false;
        double tiltRemaining = 0;
        // // The alarm starts once tilted past 45 degrees for TiltDetectSeconds, with a countdown to
        // // the emergency.
        // if (input.Tilted && _clock >= _tiltBypassUntil && Penalty != PenaltyKind.Emergency)
        // {
        //     _tiltElapsed += dt;
        //     tiltAlarm = _tiltElapsed >= _s.TiltDetectSeconds;
        //     tiltRemaining = _s.TiltDetectSeconds + _s.TiltAlarmSeconds - _tiltElapsed;
        //     if (tiltRemaining <= 0)
        //         TriggerPenalty(PenaltyKind.Emergency, $"Operator {_s.OcuLetter} down");
        // }
        // else
        // {
        //     _tiltElapsed = 0;
        // }

        if (input.Ind == IndBrake.Emerg && prev.Ind != IndBrake.Emerg && Penalty != PenaltyKind.Emergency)
            TriggerPenalty(PenaltyKind.Emergency, "Operator requested emergency");

        // An open PCS means an emergency application, whatever caused it. Once recovered, it stays
        // open until the brake pipe charges, so only a fresh opening counts while charging.
        bool pcsOpen = t is not null && t.LocoStatus1.HasFlag(LocoStatus1.PcsOpen);
        if (pcsOpen && Penalty != PenaltyKind.Emergency && (!_pcsWasOpen || !BrakeRecoveryPending))
            TriggerPenalty(PenaltyKind.Emergency, "P C S open");
        _pcsWasOpen = pcsOpen;

        // The emergency penalty centres the reverser and holds the train brake in emergency,
        // which is what Run8 needs to reset the PCS.
        if (Penalty == PenaltyKind.Emergency && stopped && linkOk)
            _pcsResetElapsed += dt;
        else
            _pcsResetElapsed = 0;
        bool pcsReady = !pcsOpen || _pcsResetElapsed >= _s.PcsResetSeconds;
        PcsResetRemaining = Penalty == PenaltyKind.Emergency && pcsOpen
            ? Math.Max(0, _s.PcsResetSeconds - _pcsResetElapsed)
            : 0;
        if (Penalty == PenaltyKind.Emergency && pcsOpen && pcsReady && !_pcsReadyAnnounced)
        {
            _pcsReadyAnnounced = true;
            Talker?.Invoke("P C S reset. Recover emergency from beltpack.");
        }

        HandleRecovery(input, prev, rscPressed, stopped, pcsReady);

        if (input.Auto == AutoBrake.Rel && prev.Auto != AutoBrake.Rel && Penalty == PenaltyKind.None)
            BrakeRecoveryPending = true;

        if (BrakeRecoveryPending && Penalty == PenaltyKind.None && t is not null
            && t.BrakePipePsi >= t.FeedValvePsi - _s.RecoveryBrakePipeTolerancePsi
            && t.AirflowCfm <= _s.RecoveryMaxAirflowCfm)
        {
            BrakeRecoveryPending = false;
            string kind = _recoveringFrom switch
            {
                PenaltyKind.Emergency => "EMERGENCY ",
                PenaltyKind.TrainBrake => "FULL SERVICE ",
                _ => "",
            };
            _recoveringFrom = PenaltyKind.None;
            ShowMessage($"{kind}BRAKE RECOVERY COMPLETE", 12);
            Talker?.Invoke("Brake recovery complete.");
        }

        if (input.Auto != AutoBrake.Charge)
        {
            _chargeAnnounced = false;
        }
        else if (!_chargeAnnounced && t is not null
            && t.BrakePipePsi >= t.FeedValvePsi - _s.RecoveryBrakePipeTolerancePsi
            && t.AirflowCfm <= _s.RecoveryMaxAirflowCfm)
        {
            _chargeAnnounced = true;
            Talker?.Invoke("Brake pipe fully charged.");
        }

        // Direction of movement protection: the reverser only changes while stopped, and moving
        // DIR while the train is moving is a full service penalty.
        var requested = ToReverser(input.Dir);
        if (!stopped && input.Dir != prev.Dir)
            TriggerPenalty(PenaltyKind.TrainBrake, "Illegal reverser change", "INVALID DIRECTION CHANGE");
        if (stopped)
            ActiveReverser = requested;
        DirLedFlashing = requested != ActiveReverser;

        // Movement needs RSC pressed within the window before selecting a speed above Coast.
        if (Penalty != PenaltyKind.None || input.Speed == SpeedSelect.Stop)
            MotionEnabled = false;
        else if (!MotionEnabled && input.Speed > SpeedSelect.Coast && _clock <= _speedSelectUntil)
            MotionEnabled = true;
        if (!MotionEnabled && Penalty == PenaltyKind.None && input.Speed > SpeedSelect.Coast && input.Speed != prev.Speed)
            ShowMessage("SPEED SELECT WITHOUT RSC", 10);

        // Brakes left applied while asking for 4 mph or more: a warning, then a service penalty.
        string? dragging = Penalty != PenaltyKind.None || input.Speed < SpeedSelect.Four ? null
            : input.Ind is IndBrake.Med or IndBrake.Full ? "IND"
            : input.Auto is >= AutoBrake.Light and <= AutoBrake.Full ? "AUTO"
            : null;
        if (dragging is null)
        {
            _dragElapsed = 0;
        }
        else
        {
            string name = dragging == "IND" ? "Independent" : "Train";
            if (_dragElapsed == 0)
                Talker?.Invoke($"{name} brakes dragging.");
            _dragElapsed += dt;
            if (_dragElapsed >= _s.DraggingBrakeSeconds)
            {
                TriggerPenalty(PenaltyKind.Service, $"{name} brakes dragging");
                dragging = null;
            }
        }
        _draggingText = dragging is null ? null : $"{dragging} BRAKES DRAGGING";

        bool lowMr = t is not null && t.MainReservoirPsi < _s.LowMainReservoirPsi;
        bool lowBp = t is not null && t.BrakePipePsi < _s.LowBrakePipePsi;
        bool powerBlocked = BrakeRecoveryPending || lowMr || lowBp || pcsOpen || input.Auto != AutoBrake.Rel
            || input.Ind != IndBrake.Rel || ActiveReverser == ReverserState.Neutral;

        if (t is not null && t.LocoStatus1.HasFlag(LocoStatus1.WheelSlip))
        {
            if (_clock >= _slipUntil)
                ShowMessage("WHEEL SLIP", 4);
            _slipUntil = _clock + 3;
        }
        bool slipping = _clock < _slipUntil;
        _slipElapsed = slipping ? _slipElapsed + dt : 0;
        if (!slipping)
        {
            _slipAnnounced = false;
        }
        else if (!_slipAnnounced && _slipElapsed >= _s.SevereWheelSlipSeconds)
        {
            _slipAnnounced = true;
            Talker?.Invoke("Severe wheel slip.");
        }

        bool tryingToCouple = input.Speed == SpeedSelect.Couple && MotionEnabled && !powerBlocked && stopped;
        _stallElapsed = tryingToCouple ? _stallElapsed + dt : 0;
        if (!tryingToCouple)
            _stallWarned = false;
        else if (!_stallWarned && _stallElapsed >= _s.TractionStallSeconds)
        {
            _stallWarned = true;
            ShowMessage("CAUTION TRAC MTR STALL", 10);
            Talker?.Invoke("Locomotive movement failure.");
        }

        if (input.HeadlightBright != prev.HeadlightBright)
            ShowMessage(input.HeadlightBright ? "HDLT BRIGHT" : "HDLT DIM", 5);

        var select = input.Speed;
        if (Penalty != PenaltyKind.None || (!MotionEnabled && select > SpeedSelect.Coast))
            select = SpeedSelect.Stop;

        bool commandingMove = select >= SpeedSelect.Couple && !powerBlocked;
        if (commandingMove && !_wasCommandingMove && stopped)
            _autoBellUntil = _clock + _s.AutoBellSeconds;
        _wasCommandingMove = commandingMove;
        int cap = powerBlocked ? 0 : _s.MaxThrottleNotch;
        if (slipping)
            cap = Math.Min(cap, Math.Max(1, _speed.Notch - 2));
        var (notch, controllerIndy) = _speed.Update(dt, select, speed, t?.AccelerationMphPerMin ?? 0, cap, t?.BrakeCylinderPsi ?? 0);

        double indPsi = IndPsi(input.Ind);
        byte indLever = _ind.LeverFor(indPsi);
        byte indy = Math.Max(indLever, controllerIndy);
        if (Penalty != PenaltyKind.None || input.Auto == AutoBrake.Charge)
            indy = 255;

        var demand = Penalty switch
        {
            PenaltyKind.Emergency => TrainBrakeDemand.Emergency,
            PenaltyKind.TrainBrake => TrainBrakeDemand.Full,
            _ => input.Auto switch
            {
                AutoBrake.Min => TrainBrakeDemand.Min,
                AutoBrake.Light => TrainBrakeDemand.Light,
                AutoBrake.Med => TrainBrakeDemand.Med,
                AutoBrake.Full => TrainBrakeDemand.Full,
                _ => TrainBrakeDemand.Release,
            },
        };
        byte trainBrake = _trainBrake.Update(dt, demand, t);

        bool trainBrakeOff = trainBrake == 0 && t is not null
            && t.BrakePipePsi >= t.FeedValvePsi - _s.RecoveryBrakePipeTolerancePsi;
        double heldPsi = !trainBrakeOff ? 0
            : indPsi > 0 && indy == indLever ? indPsi
            : _speed.BrakePsi > 0 && indy == _ind.LeverFor(_speed.BrakePsi) ? _speed.BrakePsi
            : 0;
        _ind.Trim(dt, heldPsi, t);

        bool alerterDue = _s.AutoAcknowledgeCabAlerter && t is not null
            && (t.LocoStatus1 & (LocoStatus1.AlerterPreWarning | LocoStatus1.AlerterWarning)) != 0;
        _alerterPulse = alerterDue && !_alerterPulse;

        Command = new LocoCommand
        {
            Throttle = notch,
            Indy = indy,
            TrainBrake = trainBrake,
            Reverser = Penalty == PenaltyKind.Emergency ? ReverserState.Neutral : ActiveReverser,
            Bell = input.BellHorn != BellHorn.Off || _clock < _autoBellUntil,
            Horn = input.BellHorn == BellHorn.BellHorn,
            Sander = input.Sand || _rscHeld >= _s.RscSandHoldSeconds || (slipping && _s.SandOnWheelSlip),
            HeadlightFront = (byte)(ActiveReverser == ReverserState.Fwd && input.HeadlightBright ? 2 : 1),
            HeadlightRear = (byte)(ActiveReverser == ReverserState.Rev && input.HeadlightBright ? 2 : 1),
            Alerter = _alerterPulse,
        };

        if (statusPressed)
        {
            var (shortText, spoken) = BuildStatus(input, t);
            ShowMessage(shortText, 6);
            Talker?.Invoke(spoken);
        }
        if (pitchPressed)
        {
            ShowMessage("PITCH FAILED", 3);
            Talker?.Invoke("Pitch failed. No receiving O C U.");
        }

        Alarm = tiltAlarm || rscWarning ? AlarmPattern.Continuous : AlarmPattern.None;
        SpeedLedFlashing = input.Speed > SpeedSelect.Coast && (!MotionEnabled || cap == 0);
        AutoLedFlashing = BrakeRecoveryPending || Penalty is PenaltyKind.TrainBrake;
        IndLedFlashing = Penalty is PenaltyKind.Emergency or PenaltyKind.Service;
        DisplayText = ChooseDisplay(speed, linkOk, rscWarning, tiltAlarm, tiltRemaining);
    }

    private void HandleRecovery(OcuInput input, OcuInput prev, bool rscPressed, bool stopped, bool pcsReady)
    {
        switch (Penalty)
        {
            case PenaltyKind.Service:
                // Job aid 5.4: move speed selector to STOP.
                if (input.Speed == SpeedSelect.Stop)
                    ClearPenalty(recoverBrakes: false);
                break;

            case PenaltyKind.TrainBrake:
                // Job aid 5.3: STOP, train brake FULL, RSC, then RELEASE within 3 seconds.
                if (rscPressed && input.Speed == SpeedSelect.Stop && input.Auto == AutoBrake.Full)
                    _recoveryArmedUntil = _clock + _s.RscWindowSeconds;
                if (_clock <= _recoveryArmedUntil && input.Speed == SpeedSelect.Stop
                    && input.Auto == AutoBrake.Rel && prev.Auto != AutoBrake.Rel)
                    ClearPenalty(recoverBrakes: true);
                break;

            case PenaltyKind.Emergency:
                // Job aid 5.2: STOP, train brake RELEASE, override to ES, RSC, then RELEASE within 3 seconds.
                bool waited = _clock - _penaltyAt >= _s.EmergencyMinimumSeconds && stopped && pcsReady;
                if (rscPressed && waited && input.Speed == SpeedSelect.Stop
                    && input.Auto == AutoBrake.Rel && input.Ind == IndBrake.Emerg)
                    _recoveryArmedUntil = _clock + _s.RscWindowSeconds;
                if (_clock <= _recoveryArmedUntil && input.Speed == SpeedSelect.Stop
                    && input.Ind == IndBrake.Rel && prev.Ind != IndBrake.Rel)
                    ClearPenalty(recoverBrakes: true);
                break;
        }
    }

    private void TriggerPenalty(PenaltyKind kind, string reason, string? display = null)
    {
        if (kind <= Penalty)
            return;
        Penalty = kind;
        PenaltyReason = reason;
        _penaltyAt = _clock;
        _recoveryArmedUntil = double.NegativeInfinity;
        _pcsResetElapsed = 0;
        _pcsReadyAnnounced = false;
        MotionEnabled = false;
        BrakeRecoveryPending = false;
        _recoveringFrom = PenaltyKind.None;
        _speed.Reset();
        ShowMessage(display ?? PenaltyCode(kind), display is null ? 4 : 10);
        Talker?.Invoke($"{reason}. {RecoverPhrase(kind)}.");
    }

    private void ClearPenalty(bool recoverBrakes)
    {
        var kind = Penalty;
        Penalty = PenaltyKind.None;
        PenaltyReason = "";
        _recoveryArmedUntil = double.NegativeInfinity;
        if (recoverBrakes)
        {
            BrakeRecoveryPending = true;
            _recoveringFrom = kind;
            ShowMessage("RECOVERED", 3);
        }
        else
        {
            ShowMessage("SERVICE BRAKE RECOVERY COMPLETE", 12);
        }
    }

    private void PowerDown()
    {
        Power = OcuPower.Off;
        Penalty = PenaltyKind.None;
        PenaltyReason = "";
        BrakeRecoveryPending = false;
        MotionEnabled = false;
        Alarm = AlarmPattern.None;
        DisplayText = "";
        LampTest = DisplayTest = false;
        SpeedLedFlashing = AutoLedFlashing = IndLedFlashing = DirLedFlashing = false;
        _rscElapsed = 0;
        _rscHeld = 0;
        _dragElapsed = 0;
        _draggingText = null;
        _stallElapsed = 0;
        _stallWarned = false;
        _slipElapsed = 0;
        _slipAnnounced = false;
        _chargeAnnounced = false;
        _recoveringFrom = PenaltyKind.None;
        // TILT DISABLED for the v1.2 play-test build (see Run).
        // _tiltElapsed = 0;
        _timeHeld = 0;
        _messageUntil = double.NegativeInfinity;
        _speed.Reset();
    }

    private LocoCommand SafeCommand() => new()
    {
        Throttle = 0,
        Indy = 255,
        TrainBrake = _trainBrake.Handle,
        Reverser = ActiveReverser,
    };

    private (string Short, string Spoken) BuildStatus(OcuInput input, Run8Telemetry? t)
    {
        string primary;
        if (PcsResetRemaining > 0)
            primary = $"P C S reset in {Math.Ceiling(PcsResetRemaining)} seconds";
        else if (Penalty != PenaltyKind.None)
            primary = RecoverPhrase(Penalty);
        else if (t is null)
            primary = "No communication";
        else if (t.LocoStatus1.HasFlag(LocoStatus1.PcsOpen))
            primary = "P C S open";
        else if (BrakeRecoveryPending)
            primary = t.BrakePipePsi < t.FeedValvePsi - _s.RecoveryBrakePipeTolerancePsi
                ? "Brake charging in progress"
                : "Brake recovery in progress";
        else if (t.BrakePipePsi < _s.LowBrakePipePsi)
            primary = "Low brake pipe pressure";
        else if (t.MainReservoirPsi < _s.LowMainReservoirPsi)
            primary = "Low main reservoir pressure";
        else if (input.Auto != AutoBrake.Rel)
            primary = "Release train brake from beltpack";
        else
            primary = "OK";

        string spoken = $"{SpokenUnit()}, O C U {_s.OcuLetter}. {primary}.";
        if (Penalty != PenaltyKind.None)
            spoken += $" Cause: {PenaltyReason}.";
        if (t is not null)
            spoken += $" Brake pipe {t.BrakePipePsi} P S I.";
        return (primary.ToUpperInvariant().Replace("P C S", "PCS"), spoken);
    }

    /// <summary>
    /// The railroad's initials and the unit number spoken a character at a time, as the talker
    /// does, e.g. "B N S F 4 0 2 6". Without a railroad set, "Locomotive 4 0 2 6".
    /// </summary>
    private string SpokenUnit()
    {
        static string Spell(string s) => string.Join(" ", s.Where(char.IsLetterOrDigit));
        string railroad = _s.Railroad.Trim();
        string name = railroad.Length == 0 ? "Locomotive"
            : railroad.All(char.IsUpper) ? Spell(railroad)
            : railroad;
        return $"{name} {Spell(LocomotiveId)}";
    }

    private string ChooseDisplay(double speed, bool linkOk, bool rscWarning, bool tiltAlarm, double tiltRemaining)
    {
        if (tiltAlarm)
            return $"TLT{Countdown(tiltRemaining)}";
        if (rscWarning)
            return $"RSC{Countdown(_s.RscTimeoutSeconds + _s.RscWarningSeconds - _rscElapsed)}";
        if (_clock < _messageUntil)
            return _message;
        if (_draggingText is not null)
            return _draggingText;
        if (!linkOk)
            return "NO LINK";
        if (PcsResetRemaining > 0)
            return $"PCS {Math.Ceiling(PcsResetRemaining)}";
        if (Penalty != PenaltyKind.None)
            return PenaltyCode(Penalty);
        if (BrakeRecoveryPending)
            return "CHRG";
        return speed.ToString("0.0");
    }

    private void ShowMessage(string text, double seconds)
    {
        _message = text;
        _messageUntil = _clock + seconds;
        DisplayText = text;
    }

    /// <summary>Whole seconds left, 9 down to 0, to fit beside a three-letter prefix on the display.</summary>
    private static int Countdown(double seconds) => (int)Math.Clamp(Math.Floor(seconds), 0, 9);

    private double IndPsi(IndBrake ind) => ind switch
    {
        IndBrake.Low => _s.IndLowPsi,
        IndBrake.Med => _s.IndMedPsi,
        IndBrake.Full or IndBrake.Emerg => _s.IndFullPsi,
        _ => 0,
    };

    private static ReverserState ToReverser(Direction d) => d switch
    {
        Direction.Fwd => ReverserState.Fwd,
        Direction.Rev => ReverserState.Rev,
        _ => ReverserState.Neutral,
    };

    private static string PenaltyCode(PenaltyKind kind) => kind switch
    {
        PenaltyKind.Service => "RECOVER SRVC BRK",
        PenaltyKind.TrainBrake => "RECOVER FS BRK",
        PenaltyKind.Emergency => "RECOVER EMG BRK",
        _ => "",
    };

    private static string RecoverPhrase(PenaltyKind kind) => kind switch
    {
        PenaltyKind.Service => "Recover service from beltpack",
        PenaltyKind.TrainBrake => "Recover train brake from beltpack",
        PenaltyKind.Emergency => "Recover emergency from beltpack",
        _ => "",
    };
}
