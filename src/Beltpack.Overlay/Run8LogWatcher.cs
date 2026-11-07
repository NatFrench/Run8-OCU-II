using System.Diagnostics;
using System.IO;
using System.Text;
using Beltpack.Core.Run8;

namespace Beltpack.Overlay;

/// <summary>
/// Finds the running Run8 and follows its log in the background for the locomotive the player
/// controls. The game folder comes from the Run8 process, or from settings if it can't be read.
/// </summary>
internal sealed class Run8LogWatcher : IDisposable
{
    private const string LogName = "Run8.log";

    private readonly Run8LogReader _reader = new();
    private readonly Timer _timer;
    private readonly object _gate = new();
    private string _configuredFolder = "";
    private string? _logPath;
    private long _position;

    public Run8LogWatcher() => _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

    /// <summary>The controlling unit's number, or null when Run8 isn't running or the player has no train.</summary>
    public string? ControllingUnit => _reader.ControllingUnit;

    public string ConfiguredFolder
    {
        set { lock (_gate) _configuredFolder = value ?? ""; }
    }

    private void Poll()
    {
        if (!Monitor.TryEnter(_gate))
            return;
        try
        {
            var path = FindLog();
            if (path != _logPath)
            {
                _logPath = path;
                Restart();
            }
            if (_logPath is not null)
                ReadNew(_logPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private string? FindLog()
    {
        var processes = Process.GetProcesses();
        try
        {
            var running = processes.Where(p => p.ProcessName.StartsWith("Run-8 Train Simulator", StringComparison.OrdinalIgnoreCase)).ToList();
            if (running.Count == 0)
                return null;
            foreach (var process in running)
            {
                try
                {
                    var folder = Path.GetDirectoryName(process.MainModule?.FileName);
                    if (folder is not null && File.Exists(Path.Combine(folder, LogName)))
                        return Path.Combine(folder, LogName);
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
            }
            var configured = Path.Combine(_configuredFolder, LogName);
            return _configuredFolder.Length > 0 && File.Exists(configured) ? configured : null;
        }
        finally
        {
            foreach (var p in processes)
                p.Dispose();
        }
    }

    private void Restart()
    {
        _reader.Reset();
        _position = 0;
    }

    private void ReadNew(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // The log rolls over to a fresh file at 5 MB.
        if (stream.Length < _position)
            Restart();
        if (stream.Length == _position)
            return;

        stream.Position = _position;
        var bytes = new byte[stream.Length - _position];
        int read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        // Only whole lines; a line still being written is picked up next time.
        int end = Array.LastIndexOf(bytes, (byte)'\n', read - 1);
        if (end < 0)
            return;
        foreach (var line in Encoding.UTF8.GetString(bytes, 0, end).Split('\n'))
            _reader.ReadLine(line.TrimEnd('\r'));
        _position += end + 1;
    }

    public void Dispose() => _timer.Dispose();
}
