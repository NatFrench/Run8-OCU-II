using Beltpack.Core.Ocu;
using Beltpack.Core.Rcl;

namespace Beltpack.Core.Tests;

public class RclControllerTests
{
    private const double Dt = 0.05;

    private sealed class Harness
    {
        public readonly RclSettings Settings = new();
        public readonly RclController Controller;
        public readonly FakeLoco Loco = new();
        public readonly List<string> Talk = new();
        public OcuInput Input = new() { PowerOn = true };
        public double MaxSpeedSeen;
        public double MaxBrakeCylinderSeen;

        public Harness()
        {
            Controller = new RclController(Settings);
            Controller.Talker += Talk.Add;
        }

        public void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += Dt)
            {
                Controller.Update(Dt, Input, Loco.Telemetry(), linkOk: true);
                Loco.Step(Dt, Controller.Command);
                MaxSpeedSeen = Math.Max(MaxSpeedSeen, Loco.Speed);
                MaxBrakeCylinderSeen = Math.Max(MaxBrakeCylinderSeen, Loco.BrakeCylinder);
            }
        }

        public void Press(Func<OcuInput, OcuInput> down, Func<OcuInput, OcuInput> up)
        {
            Input = down(Input);
            Run(0.2);
            Input = up(Input);
            Run(0.2);
        }

        public void PressRsc() => Press(i => i with { Rsc = true }, i => i with { Rsc = false });

        public void Set(Func<OcuInput, OcuInput> change)
        {
            Input = change(Input);
            Run(0.2);
        }

        /// <summary>Powers up and performs the train brake recovery the system reset asks for.</summary>
        public void ReadyToMove()
        {
            Input = Input with { Auto = AutoBrake.Full, Speed = SpeedSelect.Stop, Dir = Direction.Fwd };
            Run(Settings.SelfTestSeconds + 1);
            PressRsc();
            Set(i => i with { Auto = AutoBrake.Rel });
            Run(20);
        }
    }

    [Fact]
    public void PowerUpRunsSelfTestThenAsksForTrainBrakeRecovery()
    {
        var h = new Harness();

        h.Run(2);
        Assert.Equal(OcuPower.SelfTest, h.Controller.Power);
        Assert.Equal(255, h.Controller.Command.Indy);

        h.Run(h.Settings.SelfTestSeconds);
        Assert.Equal(OcuPower.Running, h.Controller.Power);
        Assert.Equal(PenaltyKind.TrainBrake, h.Controller.Penalty);
    }

    [Fact]
    public void StatusNamesTheLocomotiveFromRun8WhenKnown()
    {
        var h = new Harness();
        h.ReadyToMove();

        h.Press(i => i with { Status = true }, i => i with { Status = false });
        Assert.StartsWith("Locomotive 0 0 0 0,", h.Talk[^1]);

        h.Controller.ControllingUnit = "8081";
        h.Press(i => i with { Status = true }, i => i with { Status = false });
        Assert.StartsWith("Locomotive 8 0 8 1,", h.Talk[^1]);

        h.Settings.Railroad = "BNSF";
        h.Press(i => i with { Status = true }, i => i with { Status = false });
        Assert.StartsWith("B N S F 8 0 8 1,", h.Talk[^1]);
    }

    [Fact]
    public void TalkerUsesTheRealUnitsPhrases()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        h.Run(15);
        h.Set(i => i with { Dir = Direction.Rev });
        Assert.StartsWith("Illegal reverser change.", h.Talk[^1]);

        var e = new Harness();
        e.ReadyToMove();
        e.Set(i => i with { Ind = IndBrake.Emerg });
        Assert.StartsWith("Operator requested emergency.", e.Talk[^1]);
    }

    [Fact]
    public void ChargeAnnouncesWhenTheBrakePipeIsCharged()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.Loco.BrakePipe = 60;
        h.Set(i => i with { Dir = Direction.Neutral, Auto = AutoBrake.Charge });
        Assert.DoesNotContain(h.Talk, m => m.Contains("Brake pipe fully charged"));

        h.Run(30);
        Assert.Contains("Brake pipe fully charged.", h.Talk);
    }

    [Fact]
    public void LowBrakePipePressureBlocksPower()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.Loco.FeedValve = 50;
        h.Loco.BrakePipe = h.Loco.Equalizing = 50;
        h.Run(1);

        h.Press(i => i with { Status = true }, i => i with { Status = false });
        Assert.Contains("Low brake pipe pressure", h.Talk[^1]);
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        h.Run(10);
        Assert.Equal(0, h.Loco.Speed, 1);
    }

    [Fact]
    public void TrainBrakeRecoveryFollowsJobAidSequence()
    {
        var h = new Harness();
        h.ReadyToMove();

        Assert.Equal(PenaltyKind.None, h.Controller.Penalty);
        Assert.False(h.Controller.BrakeRecoveryPending);
        Assert.Contains(h.Talk, m => m.Contains("Brake recovery complete"));
    }

    [Fact]
    public void ReleasingTooLateAfterRscDoesNotRecover()
    {
        var h = new Harness();
        h.Input = h.Input with { Auto = AutoBrake.Full };
        h.Run(h.Settings.SelfTestSeconds + 1);

        h.PressRsc();
        h.Run(h.Settings.RscWindowSeconds + 1);
        h.Set(i => i with { Auto = AutoBrake.Rel });

        Assert.Equal(PenaltyKind.TrainBrake, h.Controller.Penalty);
    }

    [Fact]
    public void SelectingSpeedWithoutRscDoesNotMove()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.Run(5);

        h.Set(i => i with { Speed = SpeedSelect.Four });
        Assert.Equal("SPEED SELECT WITHOUT RSC", h.Controller.DisplayText);
        h.Run(15);

        Assert.Equal(0, h.Loco.Speed, 1);
        Assert.True(h.Controller.SpeedLedFlashing);
    }

    [Fact]
    public void SpeedCanBeSelectedUpToFiveSecondsAfterRsc()
    {
        var h = new Harness();
        h.ReadyToMove();

        h.PressRsc();
        h.Run(4);
        h.Set(i => i with { Speed = SpeedSelect.Couple });

        Assert.True(h.Controller.MotionEnabled);
    }

    [Theory]
    [InlineData(SpeedSelect.Couple, 1.0)]
    [InlineData(SpeedSelect.Four, 4.0)]
    [InlineData(SpeedSelect.Seven, 7.0)]
    public void HoldsSelectedSpeed(SpeedSelect select, double mph)
    {
        var h = new Harness();
        h.ReadyToMove();

        h.PressRsc();
        h.Set(i => i with { Speed = select });
        for (int i = 0; i < 3; i++)
        {
            h.Run(15);
            h.PressRsc();
        }
        Assert.True(h.MaxSpeedSeen < mph + 1.0, $"overshot to {h.MaxSpeedSeen:0.0}");

        double low = double.MaxValue, high = 0;
        for (int i = 0; i < 300; i++)
        {
            h.Run(0.1);
            low = Math.Min(low, h.Loco.Speed);
            high = Math.Max(high, h.Loco.Speed);
            if (i % 100 == 0)
                h.PressRsc();
        }
        Assert.InRange(low, mph - 0.6, mph + 0.6);
        Assert.InRange(high, mph - 0.6, mph + 0.6);
    }

    [Fact]
    public void LightEngineSettlesOnFourWithoutHunting()
    {
        // A light engine that picks up 2.5 mph/s in notch 3, with Run8's acceleration figure
        // lagging and topping out at 100 mph/min, as seen in game.
        var h = new Harness();
        h.Loco.TractionPerNotch = 0.9;
        h.Loco.TractionLagSeconds = 0.4;
        h.Loco.BrakingPerPsi = 0.1;
        h.Loco.ReportedAccelLagSeconds = 3;
        h.ReadyToMove();

        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        h.Run(15);
        h.PressRsc();
        h.Run(10);
        Assert.True(h.MaxSpeedSeen < 4.4, $"overshot to {h.MaxSpeedSeen:0.0}");

        h.MaxBrakeCylinderSeen = 0;
        double low = double.MaxValue, high = 0;
        for (int i = 0; i < 300; i++)
        {
            h.Run(0.1);
            low = Math.Min(low, h.Loco.Speed);
            high = Math.Max(high, h.Loco.Speed);
            if (i % 100 == 0)
                h.PressRsc();
        }
        Assert.InRange(low, 3.5, 4.5);
        Assert.InRange(high, 3.5, 4.5);
        Assert.True(h.MaxBrakeCylinderSeen < 5, $"braked to {h.MaxBrakeCylinderSeen:0} psi while holding");
    }

    [Theory]
    [InlineData(0.3, 1.0, 0.06, 0.0)]
    [InlineData(0.9, 0.4, 0.1, 3.0)]
    [InlineData(0.08, 1.0, 0.02, 3.0)]
    public void LoadsUpOneNotchAtATimeWithoutBackingOff(double tractionPerNotch, double tractionLag, double brakingPerPsi, double accelLag)
    {
        var h = new Harness();
        h.Loco.TractionPerNotch = tractionPerNotch;
        h.Loco.TractionLagSeconds = tractionLag;
        h.Loco.BrakingPerPsi = brakingPerPsi;
        h.Loco.ReportedAccelLagSeconds = accelLag;
        h.ReadyToMove();

        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        var notches = new List<int>();
        for (int i = 0; i < 600 && h.Loco.Speed < 3.4; i++)
        {
            h.Run(0.1);
            if (notches.Count == 0 || notches[^1] != h.Controller.Command.Throttle)
                notches.Add(h.Controller.Command.Throttle);
            if (i % 150 == 0)
                h.PressRsc();
        }

        Assert.True(h.Loco.Speed >= 3.4, $"only reached {h.Loco.Speed:0.0} mph");
        int peak = notches.IndexOf(notches.Max());
        Assert.True(notches.Take(peak + 1).SequenceEqual(Enumerable.Range(notches[0], peak + 1)),
            $"throttle went {string.Join(" ", notches)}");
        Assert.True(notches.Skip(peak).Zip(notches.Skip(peak + 1)).All(p => p.Second < p.First),
            $"throttle went {string.Join(" ", notches)}");
    }

    [Fact]
    public void HoldsSpeedOnDownhillGradeWithBrakes()
    {
        var h = new Harness();
        h.Loco.Grade = -0.15;
        h.ReadyToMove();

        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        for (int i = 0; i < 6; i++)
        {
            h.Run(15);
            h.PressRsc();
        }

        Assert.InRange(h.Loco.Speed, 3.0, 5.0);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.3)]
    public void HoldsAHeavyTrainOnAGradeThatNeedsMoreThanALightBrake(double indyCurve)
    {
        // A cut of cars on a slight grade in the yard: about 20 psi of locomotive brake just
        // balances it, and Run8's lever gives less than asked for low down.
        var h = new Harness();
        h.Loco.TractionPerNotch = 0.08;
        h.Loco.BrakingPerPsi = 0.012;
        h.Loco.Grade = -0.26;
        h.Loco.IndyCurve = indyCurve;
        h.ReadyToMove();

        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        for (int i = 0; i < 4; i++)
        {
            h.Run(15);
            h.PressRsc();
        }
        Assert.True(h.MaxSpeedSeen < 5.5, $"ran away to {h.MaxSpeedSeen:0.0}");

        double low = double.MaxValue, high = 0;
        var throttle = new HashSet<int>();
        for (int i = 0; i < 600; i++)
        {
            h.Run(0.1);
            low = Math.Min(low, h.Loco.Speed);
            high = Math.Max(high, h.Loco.Speed);
            throttle.Add(h.Controller.Command.Throttle);
            if (i % 100 == 0)
                h.PressRsc();
        }
        Assert.InRange(low, 3.4, 4.6);
        Assert.InRange(high, 3.4, 4.6);
        Assert.Equal([0], throttle);
    }

    [Fact]
    public void CoastBCutsPowerAndAppliesALightLocomotiveBrake()
    {
        var h = new Harness();
        h.ReadyToMove();

        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Couple });
        h.Run(15);
        h.PressRsc();

        h.Set(i => i with { Speed = SpeedSelect.CoastB });
        Assert.Equal(0, h.Controller.Command.Throttle);
        h.Run(5);
        Assert.InRange(h.Loco.BrakeCylinder, h.Settings.CoastBPsi - 2, h.Settings.CoastBPsi + 2);
    }

    [Theory]
    [InlineData(SpeedSelect.Coast)]
    [InlineData(SpeedSelect.CoastB)]
    public void CoastPositionsBrakeDownToTwoMph(SpeedSelect coast)
    {
        var h = new Harness();
        h.ReadyToMove();

        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Seven });
        h.Run(30);
        h.PressRsc();
        Assert.True(h.Loco.Speed > 6, $"only reached {h.Loco.Speed:0.0} mph");

        h.MaxBrakeCylinderSeen = 0;
        h.Set(i => i with { Speed = coast });
        h.Run(15);
        h.PressRsc();
        h.Run(10);

        Assert.True(h.Loco.Speed <= h.Settings.CoastLimitMph + 0.5, $"still {h.Loco.Speed:0.0} mph");
        Assert.Equal(0, h.Controller.Command.Throttle);
        // No harder than it takes to slow at the too-fast rate, plus a step.
        double gentle = h.Settings.BrakeTooFastMphPerSecond / h.Loco.BrakingPerPsi + h.Settings.BrakeStepPsi
            + (coast == SpeedSelect.CoastB ? h.Settings.CoastBPsi : 0);
        Assert.True(h.MaxBrakeCylinderSeen <= gentle + 3, $"braked to {h.MaxBrakeCylinderSeen:0} psi");
    }

    [Theory]
    [InlineData(IndBrake.Low, 71)]
    [InlineData(IndBrake.Med, 71)]
    [InlineData(IndBrake.Full, 71)]
    [InlineData(IndBrake.Med, 40)]
    [InlineData(IndBrake.Med, 120)]
    public void IndPositionsHoldTheirBrakeCylinderPressure(IndBrake ind, double assumedFullScalePsi)
    {
        var h = new Harness();
        h.Settings.IndyFullScalePsi = assumedFullScalePsi;
        h.ReadyToMove();

        h.Set(i => i with { Speed = SpeedSelect.Coast, Ind = ind });
        h.Run(20);

        double psi = ind switch
        {
            IndBrake.Low => h.Settings.IndLowPsi,
            IndBrake.Med => h.Settings.IndMedPsi,
            _ => h.Settings.IndFullPsi,
        };
        Assert.InRange(h.Loco.BrakeCylinder, psi - 2, psi + 2);
    }

    [Fact]
    public void StopSelectorBringsTrainToRest()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Seven });
        h.Run(30);

        h.Set(i => i with { Speed = SpeedSelect.Stop });
        h.Run(20);

        Assert.Equal(0, h.Loco.Speed, 1);
        Assert.False(h.Controller.MotionEnabled);
    }

    [Fact]
    public void StopSlowsAtASteadyRateThenHoldsWithFullIndependent()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Seven });
        h.Run(30);
        double from = h.Loco.Speed;
        Assert.True(from > 6, $"only reached {from:0.0} mph");

        h.Input = h.Input with { Speed = SpeedSelect.Stop };
        double elapsed = 0, firstSecondMaxIndy = 0;
        while (h.Loco.Speed > 0.05 && elapsed < 30)
        {
            h.Run(Dt);
            elapsed += Dt;
            if (elapsed <= 1)
                firstSecondMaxIndy = Math.Max(firstSecondMaxIndy, h.Controller.Command.Indy);
        }

        Assert.True(firstSecondMaxIndy < h.Settings.StopIndy, "applied the full brake straight away");
        double rate = from / elapsed;
        Assert.InRange(rate, 0.6, 1.4);

        h.Run(2);
        Assert.Equal(h.Settings.StopIndy, h.Controller.Command.Indy);
    }

    [Fact]
    public void RscTimeoutWarnsThenAppliesTrainBrakePenalty()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });

        h.Run(h.Settings.RscTimeoutSeconds + 1);
        Assert.Equal(AlarmPattern.Continuous, h.Controller.Alarm);
        Assert.Equal(PenaltyKind.None, h.Controller.Penalty);

        h.Run(h.Settings.RscWarningSeconds);
        Assert.Equal(PenaltyKind.TrainBrake, h.Controller.Penalty);
        Assert.Equal(0, h.Controller.Command.Throttle);

        h.Run(15);
        Assert.True(h.Loco.BrakePipe <= 90 - 25, $"brake pipe only fell to {h.Loco.BrakePipe:0}");
        Assert.Equal(PenaltyKind.TrainBrake, h.Controller.Penalty);
    }

    [Fact]
    public void OnlyRscResetsTheWarningOnceItSounds()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });

        h.Run(h.Settings.RscTimeoutSeconds + 1);
        Assert.StartsWith("RSC", h.Controller.DisplayText);
        h.Set(i => i with { BellHorn = BellHorn.Bell });
        h.Run(h.Settings.RscWarningSeconds);

        Assert.Equal(PenaltyKind.TrainBrake, h.Controller.Penalty);
    }

    [Fact]
    public void HoldingRscSands()
    {
        var h = new Harness();
        h.ReadyToMove();

        h.Set(i => i with { Rsc = true });
        Assert.False(h.Controller.Command.Sander);
        h.Run(h.Settings.RscSandHoldSeconds);
        Assert.True(h.Controller.Command.Sander);
        h.Set(i => i with { Rsc = false });
        Assert.False(h.Controller.Command.Sander);
    }

    [Theory]
    [InlineData(IndBrake.Med, AutoBrake.Rel, "IND BRAKES DRAGGING")]
    [InlineData(IndBrake.Rel, AutoBrake.Light, "AUTO BRAKES DRAGGING")]
    public void DraggingBrakesWarnThenApplyServicePenalty(IndBrake ind, AutoBrake auto, string message)
    {
        var h = new Harness();
        h.ReadyToMove();
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        h.Run(10);

        h.Set(i => i with { Ind = ind, Auto = auto });
        h.Run(5);
        Assert.Equal(message, h.Controller.DisplayText);
        Assert.Equal(PenaltyKind.None, h.Controller.Penalty);
        h.PressRsc();
        h.Run(h.Settings.DraggingBrakeSeconds);
        Assert.Equal(PenaltyKind.Service, h.Controller.Penalty);

        h.Set(i => i with { Speed = SpeedSelect.Stop });
        Assert.Equal(PenaltyKind.None, h.Controller.Penalty);
    }

    [Fact]
    public void BackingOffDraggingBrakesInTimeAvoidsThePenalty()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        h.Run(10);

        h.Set(i => i with { Ind = IndBrake.Med });
        h.Run(h.Settings.DraggingBrakeSeconds - 5);
        h.Set(i => i with { Ind = IndBrake.Low });
        h.Run(10);

        Assert.Equal(PenaltyKind.None, h.Controller.Penalty);
    }

    [Fact]
    public void RscTestInCoastWithReverserNeutralFollowsJobAid()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.Set(i => i with { Dir = Direction.Neutral });
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Coast });

        h.Run(h.Settings.RscTimeoutSeconds + 1);
        Assert.Equal(AlarmPattern.Continuous, h.Controller.Alarm);

        h.Run(h.Settings.RscWarningSeconds);
        Assert.Equal(PenaltyKind.TrainBrake, h.Controller.Penalty);
        Assert.Equal(0, h.Loco.Speed, 1);
    }

    [Fact]
    public void FullServicePenaltyIsRampedToAvoidUndesiredEmergency()
    {
        var h = new Harness();
        h.Run(h.Settings.SelfTestSeconds + 0.5);
        Assert.Equal(PenaltyKind.TrainBrake, h.Controller.Penalty);
        Assert.True(h.Controller.Command.TrainBrake < 60, $"handle jumped to {h.Controller.Command.TrainBrake}");

        h.Run(20);
        Assert.Equal(0, h.Loco.UndesiredEmergencies);
        Assert.Equal(h.Settings.TrainBrakeFullService, h.Controller.Command.TrainBrake);
        Assert.InRange(h.Loco.BrakePipe, 63, 66);
    }

    [Fact]
    public void JumpingStraightToFullServiceTripsTheFakeLikeRun8()
    {
        var h = new Harness();
        h.Settings.TrainBrakeApplyRate = 10_000;
        h.Run(h.Settings.SelfTestSeconds + 10);

        Assert.Equal(1, h.Loco.UndesiredEmergencies);
        Assert.Equal(PenaltyKind.Emergency, h.Controller.Penalty);
    }

    [Fact]
    public void HoldingTimeBeepsOnceWithoutALink()
    {
        var settings = new RclSettings();
        var controller = new RclController(settings);
        int beeps = 0;
        controller.Sound += _ => beeps++;
        var input = new OcuInput { PowerOn = true };
        void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += Dt)
                controller.Update(Dt, input, null, linkOk: false);
        }

        Run(settings.SelfTestSeconds + 1);
        input = input with { TiltTimeout = true };
        Run(0.5);
        input = input with { TiltTimeout = false };
        Run(0.5);
        Assert.Equal(0, beeps);

        input = input with { TiltTimeout = true };
        Run(settings.TiltTimeoutHoldSeconds + 1);
        Assert.Equal(1, beeps);
        Assert.Equal("TIME", controller.DisplayText);
    }

    private const string TiltDisabled = "Tilt is disabled for the v1.2 play-test build";

    [Fact(Skip = TiltDisabled)]
    public void TiltAppliesEmergencyUnlessTimeOutPressed()
    {
        var h = new Harness();
        h.ReadyToMove();

        h.Set(i => i with { TiltTimeout = true });
        h.Run(h.Settings.TiltTimeoutHoldSeconds);
        Assert.Equal("TIME", h.Controller.DisplayText);
        h.Set(i => i with { TiltTimeout = false });
        h.Set(i => i with { Tilted = true });
        h.Run(h.Settings.TiltDetectSeconds + h.Settings.TiltAlarmSeconds + 2);
        Assert.Equal(PenaltyKind.None, h.Controller.Penalty);

        h.Run(h.Settings.TiltBypassSeconds);
        Assert.Equal(PenaltyKind.Emergency, h.Controller.Penalty);
        Assert.Equal(255, h.Controller.Command.TrainBrake);
    }

    [Fact(Skip = TiltDisabled)]
    public void TappingTimeDoesNotBypassTilt()
    {
        var h = new Harness();
        h.ReadyToMove();

        h.Press(i => i with { TiltTimeout = true }, i => i with { TiltTimeout = false });
        h.Set(i => i with { Tilted = true });
        h.Run(h.Settings.TiltDetectSeconds + h.Settings.TiltAlarmSeconds + 1);
        Assert.Equal(PenaltyKind.Emergency, h.Controller.Penalty);
    }

    [Fact]
    public void EmergencyRecoveryNeedsWaitThenSequence()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.Set(i => i with { Ind = IndBrake.Emerg });
        Assert.Equal(PenaltyKind.Emergency, h.Controller.Penalty);

        h.PressRsc();
        h.Set(i => i with { Ind = IndBrake.Rel });
        Assert.Equal(PenaltyKind.Emergency, h.Controller.Penalty);

        h.Set(i => i with { Ind = IndBrake.Emerg });
        h.Run(h.Settings.PcsResetSeconds);
        h.PressRsc();
        h.Set(i => i with { Ind = IndBrake.Rel });

        Assert.Equal(PenaltyKind.None, h.Controller.Penalty);
        Assert.True(h.Controller.BrakeRecoveryPending);
        Assert.Equal(0, h.Controller.Command.TrainBrake);

        h.Run(30);
        Assert.False(h.Loco.PcsOpen);
        Assert.False(h.Controller.BrakeRecoveryPending);
    }

    [Fact]
    public void OpenPcsAppliesEmergencyAndWaitsForRun8Reset()
    {
        var h = new Harness();
        h.ReadyToMove();

        h.Loco.PcsOpen = true;
        h.Run(0.2);
        Assert.Equal(PenaltyKind.Emergency, h.Controller.Penalty);
        Assert.Equal(ReverserState.Neutral, h.Controller.Command.Reverser);
        Assert.Equal(255, h.Controller.Command.TrainBrake);
        h.Run(4.5);
        Assert.StartsWith("PCS", h.Controller.DisplayText);

        h.Set(i => i with { Ind = IndBrake.Emerg });
        h.Run(h.Settings.EmergencyMinimumSeconds);
        h.PressRsc();
        h.Set(i => i with { Ind = IndBrake.Rel });
        Assert.Equal(PenaltyKind.Emergency, h.Controller.Penalty);

        h.Set(i => i with { Ind = IndBrake.Emerg });
        h.Run(h.Settings.PcsResetSeconds - h.Settings.EmergencyMinimumSeconds);
        Assert.Contains(h.Talk, m => m.Contains("P C S reset"));
        h.PressRsc();
        h.Set(i => i with { Ind = IndBrake.Rel });
        h.Run(30);

        Assert.Equal(PenaltyKind.None, h.Controller.Penalty);
        Assert.False(h.Loco.PcsOpen);
        Assert.False(h.Controller.BrakeRecoveryPending);

        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        h.Run(15);
        Assert.Equal(ReverserState.Fwd, h.Controller.Command.Reverser);
        Assert.True(h.Loco.Speed > 2, $"only reached {h.Loco.Speed:0.0} mph");
    }

    [Fact]
    public void BellRingsForTwoSecondsAsSoonAsAStoppedTrainIsToldToMove()
    {
        var h = new Harness();
        h.ReadyToMove();
        Assert.False(h.Controller.Command.Bell);

        h.PressRsc();
        h.Input = h.Input with { Speed = SpeedSelect.Four };
        h.Run(0.05);
        Assert.True(h.Controller.Command.Bell);
        Assert.True(h.Loco.Speed < 0.2);

        h.Run(1.8);
        Assert.True(h.Controller.Command.Bell);
        h.Run(0.3);
        Assert.False(h.Controller.Command.Bell);
    }

    [Fact]
    public void BellWaitsUntilPowerIsAllowed()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.Set(i => i with { Ind = IndBrake.Full });
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Couple });
        h.Run(2);
        Assert.False(h.Controller.Command.Bell);

        h.Set(i => i with { Ind = IndBrake.Rel });

        Assert.True(h.Controller.Command.Bell);
    }

    [Fact]
    public void BellDoesNotRingAgainWhileMoving()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        h.Run(15);
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Seven });

        for (int i = 0; i < 100; i++)
        {
            h.Run(0.1);
            Assert.False(h.Controller.Command.Bell);
        }
    }

    [Fact]
    public void DirectionChangeWhileMovingIsAFullServicePenalty()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Four });
        h.Run(15);

        h.Set(i => i with { Dir = Direction.Rev });

        Assert.Equal(PenaltyKind.TrainBrake, h.Controller.Penalty);
        Assert.Equal("INVALID DIRECTION CHANGE", h.Controller.DisplayText);
        Assert.Equal(ReverserState.Fwd, h.Controller.Command.Reverser);
        Assert.True(h.Controller.DirLedFlashing);
    }

    [Fact]
    public void TrainBrakeApplicationCutsPower()
    {
        var h = new Harness();
        h.ReadyToMove();
        h.PressRsc();
        h.Set(i => i with { Speed = SpeedSelect.Seven });
        h.Run(20);

        h.Set(i => i with { Auto = AutoBrake.Full });
        h.Run(30);

        Assert.Equal(0, h.Controller.Command.Throttle);
        Assert.Equal(0, h.Loco.Speed, 1);
    }

    [Fact]
    public void PowerOffAppliesLocomotiveBrakes()
    {
        var h = new Harness();
        h.ReadyToMove();

        h.Set(i => i with { PowerOn = false });

        Assert.Equal(OcuPower.Off, h.Controller.Power);
        Assert.Equal(0, h.Controller.Command.Throttle);
        Assert.Equal(255, h.Controller.Command.Indy);
    }
}
