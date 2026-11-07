namespace Beltpack.Core.Rcl;

/// <summary>
/// Tunables for the emulated on-board ("Brain in the Train") logic. Values marked as guesses
/// are not documented anywhere public and should be adjusted once someone who ran the real
/// equipment can confirm them.
/// </summary>
public sealed class RclSettings
{
    public int Run8Port { get; set; } = 18888;

    public string LocomotiveId { get; set; } = "0000";
    // Spoken before the unit number in talker messages, e.g. "BNSF". Empty says "Locomotive".
    public string Railroad { get; set; } = "";
    public string OcuLetter { get; set; } = "A";
    public string Channel { get; set; } = "F1";

    // Speed selector targets in mph. Couple ~1 mph per the CANAC job aid; the rest are typical
    // settings, and railroads could have others programmed.
    public double CoupleMph { get; set; } = 1.0;
    public double FourMph { get; set; } = 4.0;
    public double SevenMph { get; set; } = 7.0;
    public double TenMph { get; set; } = 10.0;
    public double MaxMph { get; set; } = 15.0;

    // Speed control loads up one notch at a time towards a ramp of about half a mph per second,
    // whatever the train, holding the notch while the train picks up between the too-slow and
    // too-fast rates. Tuned against Run8 V3 telemetry on a light engine, which picks up about
    // 1 mph/s in notch 1 and carries on accelerating for about half a second after power comes off.
    public int MaxThrottleNotch { get; set; } = 8;
    public double RampTooSlowMphPerSecond { get; set; } = 0.25;
    public double RampTooFastMphPerSecond { get; set; } = 1.0;
    public double ThrottleStepUpSeconds { get; set; } = 2.0;
    public double ThrottleStepDownSeconds { get; set; } = 1.5;
    public double ArrivalSeconds { get; set; } = 0.5;
    public double ArrivalStepDownSeconds { get; set; } = 0.5;
    // Once at the selected speed, power isn't added back until the train has dropped this far
    // below it or is clearly slowing.
    public double HoldBandMph { get; set; } = 0.5;
    public double HoldDropMphPerSecond { get; set; } = 0.05;
    public int ReleasingBrakeCylinderPsi { get; set; } = 8;
    // More than OverspeedDeadbandMph over the selected speed (or CoastLimitMph in Coast), the
    // locomotive brake is applied a step at a time, the same way as the throttle: another step only
    // if the train isn't slowing by the too-slow rate, one off if it slows faster than the too-fast
    // rate, and off a step at a time as it gets back to the selected speed. Back at the selected
    // speed, a step is added only if the train picks up again, so it can hold a train on a grade.
    // There's no limit short of full locomotive brake.
    public double OverspeedDeadbandMph { get; set; } = 0.5;
    public double BrakeStepPsi { get; set; } = 4;
    public double BrakeTooSlowMphPerSecond { get; set; } = 0.1;
    public double BrakeTooFastMphPerSecond { get; set; } = 0.6;
    public double BrakeStepUpSeconds { get; set; } = 1.0;
    public double BrakeStepDownSeconds { get; set; } = 1.5;
    public double BrakeArrivalStepSeconds { get; set; } = 1.0;
    public byte StopIndy { get; set; } = 255;
    // Stop brings a moving train to rest at a steady rate on the locomotive brake, then holds it with
    // StopIndy. The rate and how the brake is worked to hold it are guesses.
    public double StopDecelMphPerSecond { get; set; } = 1.0;
    public double StopHoldMph { get; set; } = 0.5;
    public byte StopStartIndy { get; set; } = 60;
    public double StopIndyPerMphPerSecond { get; set; } = 60.0;
    // Coast B: no power and a light locomotive brake, which stretches the slack and eases the train
    // down on a shove (a BNSF operator). Coast and Coast B both use the locomotive brake to keep
    // the train at or under CoastLimitMph.
    public double CoastBPsi { get; set; } = 15;
    public double CoastLimitMph { get; set; } = 2.0;

    // IND (brake override) positions as locomotive brake cylinder psi.
    public double IndLowPsi { get; set; } = 15;
    public double IndMedPsi { get; set; } = 30;
    public double IndFullPsi { get; set; } = 45;
    // Run8 V3 holds 71 psi with the independent lever fully applied.
    public double IndyFullScalePsi { get; set; } = 71;
    public bool TrimIndyFromTelemetry { get; set; } = true;

    // Automatic brake handle bytes (0 release .. 255 emergency). Measured in Run8 V3 on a GP-class
    // unit: 26 gives a 7 psi reduction, 250 gives 26 psi, roughly linear in between.
    public byte TrainBrakeMinReduction { get; set; } = 26;
    public byte TrainBrakeFullService { get; set; } = 250;

    /// <summary>
    /// Handle units per second when applying. Jumping straight from release to full service trips
    /// an undesired emergency in Run8 on a light engine; 25/s (about 10 s to full) does not.
    /// </summary>
    public double TrainBrakeApplyRate { get; set; } = 25;
    public double MinReductionPsi { get; set; } = 7;
    public double LightReductionPsi { get; set; } = 12;
    public double MedReductionPsi { get; set; } = 18;
    public double FullReductionPsi { get; set; } = 26;
    public bool TrimTrainBrakeFromTelemetry { get; set; } = true;

    // Timers from the CANAC job aid unless noted.
    public double SelfTestSeconds { get; set; } = 10;
    public double RscTimeoutSeconds { get; set; } = 50;
    public double RscWarningSeconds { get; set; } = 10;
    // Until the RSC warning sounds; after that only RSC resets it.
    public bool AnyControlResetsRsc { get; set; } = true;
    // Recovery steps after RSC.
    public double RscWindowSeconds { get; set; } = 3;
    public double TiltBypassSeconds { get; set; } = 60;
    // The job aid has Time held "until double beeping alarm sounds"; a real unit gives one short beep
    // after Time has been held for about 2 s.
    public double TiltTimeoutHoldSeconds { get; set; } = 2;
    public double EmergencyMinimumSeconds { get; set; } = 60;

    // Typical railroad settings rather than the job aid.
    public double SpeedSelectWindowSeconds { get; set; } = 5;
    public double RscSandHoldSeconds { get; set; } = 2;
    public double TiltDetectSeconds { get; set; } = 1;
    // Measured on a real unit: the alarm sounds for 3.75 s, then the emergency applies.
    public double TiltAlarmSeconds { get; set; } = 3.75;
    public double DraggingBrakeSeconds { get; set; } = 30;
    public double TractionStallSeconds { get; set; } = 20;
    // Bell rung automatically when a stopped train is told to move; 0 turns it off.
    public double AutoBellSeconds { get; set; } = 2;

    /// <summary>
    /// Run8 only resets an open PCS after the reverser has been centred and the train brake fully
    /// applied for this long, followed by a release.
    /// </summary>
    public double PcsResetSeconds { get; set; } = 120;
    public double CommLossSeconds { get; set; } = 3;

    public int RecoveryBrakePipeTolerancePsi { get; set; } = 4;
    public int RecoveryMaxAirflowCfm { get; set; } = 20;
    public int LowMainReservoirPsi { get; set; } = 110;
    public int LowBrakePipePsi { get; set; } = 55;
    // How long wheel slip lasts before the talker calls it severe. A guess.
    public double SevereWheelSlipSeconds { get; set; } = 3;

    public bool AutoAcknowledgeCabAlerter { get; set; } = true;
    public bool SandOnWheelSlip { get; set; } = true;

    public double TargetMph(Ocu.SpeedSelect s) => s switch
    {
        Ocu.SpeedSelect.Couple => CoupleMph,
        Ocu.SpeedSelect.Four => FourMph,
        Ocu.SpeedSelect.Seven => SevenMph,
        Ocu.SpeedSelect.Ten => TenMph,
        Ocu.SpeedSelect.Max => MaxMph,
        _ => 0,
    };
}
