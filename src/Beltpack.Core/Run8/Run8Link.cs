using System.Net;
using System.Net.Sockets;

namespace Beltpack.Core.Run8;

/// <summary>
/// Sends control messages to Run8 on the configured port and listens for telemetry on port + 1.
/// Run8 only talks over the loopback address.
/// </summary>
public sealed class Run8Link : IDisposable
{
    private readonly UdpClient _sender = new();
    private readonly UdpClient _listener;
    private readonly IPEndPoint _target;
    private readonly CancellationTokenSource _cts = new();
    private Run8Telemetry? _latest;
    private long _lastTelemetryTicks;

    public Run8Link(int port)
    {
        _target = new IPEndPoint(IPAddress.Loopback, port);
        _listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, port + 1));
        _ = Task.Run(ReceiveLoop);
    }

    public Run8Telemetry? Latest => Volatile.Read(ref _latest);

    public TimeSpan TelemetryAge
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastTelemetryTicks);
            return ticks == 0 ? TimeSpan.MaxValue : TimeSpan.FromTicks(DateTime.UtcNow.Ticks - ticks);
        }
    }

    public void Send(Run8Message type, byte value, bool playAudio = false)
    {
        var msg = Run8Protocol.BuildInput(type, value, playAudio);
        try
        {
            _sender.Send(msg, msg.Length, _target);
        }
        catch (SocketException)
        {
            // Run8 not listening yet; the next refresh will retry.
        }
    }

    private async Task ReceiveLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _listener.ReceiveAsync(_cts.Token);
                if (Run8Protocol.TryParseTelemetry(result.Buffer, out var telemetry))
                {
                    Volatile.Write(ref _latest, telemetry);
                    Interlocked.Exchange(ref _lastTelemetryTicks, DateTime.UtcNow.Ticks);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                // ICMP port-unreachable resets surface here on Windows; keep listening.
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Dispose();
        _sender.Dispose();
        _cts.Dispose();
    }
}
