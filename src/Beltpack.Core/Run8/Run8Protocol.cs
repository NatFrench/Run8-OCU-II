namespace Beltpack.Core.Run8;

/// <summary>
/// Message types accepted by Run8's Custom Input Device UDP interface (UserGuide\CustomInputDevice.pdf).
/// </summary>
public enum Run8Message : ushort
{
    Alerter = 1,
    Bell = 2,
    DynBrakeLever = 4,
    HeadlightFront = 5,
    HeadlightRear = 6,
    Horn = 8,
    IndyBrakeLever = 9,
    IndyBailoff = 10,
    IsolationSwitch = 11,
    ParkBrakeSet = 12,
    ParkBrakeRelease = 13,
    ReverserLever = 14,
    Sander = 15,
    ThrottleLever = 16,
    TrainBrakeLever = 18,
    SlowSpeedOnOff = 55,
}

public static class Run8Protocol
{
    public const byte InputHeader = 96;
    public const byte InputHeaderWithAudio = 224;
    public const byte OutputHeader = 97;

    public const byte ReverserReverse = 0;
    public const byte ReverserNeutral = 128;
    public const byte ReverserForward = 255;

    public static byte[] BuildInput(Run8Message type, byte value, bool playAudio = false)
    {
        var msg = new byte[5];
        msg[0] = playAudio ? InputHeaderWithAudio : InputHeader;
        msg[1] = (byte)((ushort)type >> 8);
        msg[2] = (byte)((ushort)type & 0xFF);
        msg[3] = value;
        msg[4] = Crc(msg, msg.Length - 1);
        return msg;
    }

    /// <summary>XOR of the first <paramref name="count"/> bytes, starting from the header.</summary>
    public static byte Crc(ReadOnlySpan<byte> data, int count)
    {
        byte crc = data[0];
        for (int i = 1; i < count; i++)
            crc ^= data[i];
        return crc;
    }

    public static bool TryParseTelemetry(ReadOnlySpan<byte> packet, out Run8Telemetry telemetry)
    {
        telemetry = default!;
        if (packet.Length < 27 || packet[0] != OutputHeader)
            return false;
        if (Crc(packet, packet.Length - 1) != packet[^1])
            return false;

        telemetry = new Run8Telemetry
        {
            SpeedMph = DecodeSpeed(packet[1]),
            MainReservoirPsi = packet[2],
            EqualizingReservoirPsi = packet[3],
            BrakePipePsi = packet[4],
            BrakeCylinderPsi = packet[6],
            AirflowCfm = packet[7],
            ThrottleNotch = packet[8],
            EotBrakePipePsi = packet[9],
            HasEot = packet[10] != 255,
            LocoStatus1 = (LocoStatus1)packet[15],
            Ammeter = packet[16],
            AccelerationMphPerMin = DecodeAcceleration(packet[19]),
            Reverser = packet[20],
            DieselRpm = packet[22] * 10,
            LocoStatus2 = (LocoStatus2)packet[23],
            FeedValvePsi = packet[25],
        };
        return true;
    }

    /// <summary>Below 10 mph the 128 bit is set and the value is tenths of a mph.</summary>
    public static double DecodeSpeed(byte b) => b >= 128 ? (b - 128) / 10.0 : b;

    public static double DecodeAcceleration(byte b) => (b / 255.0 * 2.0 - 1.0) * 100.0;
}

[Flags]
public enum LocoStatus1 : byte
{
    ParkingBrake = 1,
    WheelSlip = 2,
    PcsOpen = 4,
    Sander = 8,
    AlerterPreWarning = 16,
    AlerterWarning = 32,
    HornOn = 64,
    BellOn = 128,
}

[Flags]
public enum LocoStatus2 : byte
{
    PenaltyBrake = 1,
    DynamicBrakeMode = 2,
    AirCompressorOn = 4,
}

public sealed record Run8Telemetry
{
    public double SpeedMph { get; init; }
    public int MainReservoirPsi { get; init; }
    public int EqualizingReservoirPsi { get; init; }
    public int BrakePipePsi { get; init; }
    public int BrakeCylinderPsi { get; init; }
    public int AirflowCfm { get; init; }
    public int ThrottleNotch { get; init; }
    public int EotBrakePipePsi { get; init; }
    public bool HasEot { get; init; }
    public LocoStatus1 LocoStatus1 { get; init; }
    public int Ammeter { get; init; }
    public double AccelerationMphPerMin { get; init; }
    public byte Reverser { get; init; }
    public int DieselRpm { get; init; }
    public LocoStatus2 LocoStatus2 { get; init; }
    public int FeedValvePsi { get; init; }
}
