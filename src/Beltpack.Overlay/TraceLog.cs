using System.IO;
using Beltpack.Core.Ocu;
using Beltpack.Core.Rcl;
using Beltpack.Core.Run8;

namespace Beltpack.Overlay;

/// <summary>
/// Appends a line to events.log whenever the controller state, the command sent to Run8, or Run8's
/// status flags change, so odd behaviour in the sim can be traced afterwards. Past MaxBytes the log
/// moves to events.old.log (replacing the previous one) and starts afresh, so it never grows without
/// bound.
/// </summary>
public sealed class TraceLog : IDisposable
{
    public static readonly string FilePath = Path.Combine(Path.GetDirectoryName(OverlaySettings.FilePath)!, "events.log");
    private static readonly string OldPath = Path.Combine(Path.GetDirectoryName(FilePath)!, "events.old.log");
    private const long MaxBytes = 5 * 1024 * 1024;

    private readonly object _lock = new();
    private StreamWriter? _writer;
    private string _last = "";

    public TraceLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                File.Move(FilePath, OldPath, overwrite: true);
            Open();
            _writer?.WriteLine($"--- overlay started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        catch (IOException)
        {
        }
    }

    private void Open() => _writer = new StreamWriter(FilePath, append: true) { AutoFlush = true };

    private void RollOver()
    {
        try
        {
            _writer?.Dispose();
            _writer = null;
            File.Move(FilePath, OldPath, overwrite: true);
            Open();
            _writer?.WriteLine($"--- log continued {DateTime.Now:yyyy-MM-dd HH:mm:ss} (earlier lines in events.old.log)");
        }
        catch (IOException)
        {
        }
    }

    public void Record(RclController c, OcuInput input, bool beltpack, Run8Telemetry? t)
    {
        lock (_lock)
            RecordLocked(c, input, beltpack, t);
    }

    private void RecordLocked(RclController c, OcuInput input, bool beltpack, Run8Telemetry? t)
    {
        if (_writer is null)
            return;
        var cmd = c.Command;
        string state = $"{c.Power} {(beltpack ? "BELTPACK" : "MANUAL")} pen={c.Penalty} rec={c.BrakeRecoveryPending} sel={input.Speed} " +
                       $"motion={c.MotionEnabled} | cmd thr={cmd.Throttle} indy={cmd.Indy} tb={cmd.TrainBrake} rev={cmd.Reverser} " +
                       $"alerter={cmd.Alerter} | run8 {t?.LocoStatus1}/{t?.LocoStatus2} rev={t?.Reverser}";
        if (state == _last)
            return;
        _last = state;
        string air = t is null ? "no telemetry" : $"{t.SpeedMph:0.0}mph ER {t.EqualizingReservoirPsi} BP {t.BrakePipePsi} BC {t.BrakeCylinderPsi} MR {t.MainReservoirPsi}";
        _writer.WriteLine($"{DateTime.Now:HH:mm:ss.f} {state} | {air}");
        if (_writer.BaseStream.Length > MaxBytes)
            RollOver();
    }

    // The talker notes things from background threads, and can do so after the log is closed.
    public void Note(string text)
    {
        lock (_lock)
            _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.f} {text}");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
