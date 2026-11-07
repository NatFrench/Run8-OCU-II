using Beltpack.Core.Rcl;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Beltpack.Overlay;

/// <summary>
/// The OCU's piezo: the short beep and the steady alarm, with pitch and length measured on a real
/// OCU II.
/// </summary>
public sealed class SoundEngine : IDisposable
{
    private const int SampleRate = 44100;
    private const double PiezoHz = 3640;

    private readonly WaveOutEvent _output = new();
    private readonly MixingSampleProvider _mixer;
    private readonly AlarmProvider _alarm;

    public SoundEngine(float volume)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
        _mixer = new MixingSampleProvider(format) { ReadFully = true };
        _alarm = new AlarmProvider(format);
        _mixer.AddMixerInput(_alarm);
        _output.DesiredLatency = 80;
        _output.Init(new VolumeSampleProvider(_mixer) { Volume = volume });
        _output.Play();
    }

    public void SetAlarm(AlarmPattern pattern) => _alarm.Pattern = pattern;

    public void Play(OcuSound sound)
    {
        var segments = sound switch
        {
            OcuSound.ShortBeep => new[] { (PiezoHz, 0.27) },
            _ => Array.Empty<(double, double)>(),
        };
        _mixer.AddMixerInput(new ToneProvider(_mixer.WaveFormat, segments));
    }

    public void Dispose()
    {
        _output.Stop();
        _output.Dispose();
    }

    private sealed class ToneProvider : ISampleProvider
    {
        private readonly (double Hz, int Samples)[] _segments;
        private int _segment;
        private int _position;
        private double _phase;

        public ToneProvider(WaveFormat format, (double Hz, double Seconds)[] segments)
        {
            WaveFormat = format;
            _segments = segments.Select(s => (s.Hz, (int)(s.Seconds * format.SampleRate))).ToArray();
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int written = 0;
            while (written < count && _segment < _segments.Length)
            {
                var (hz, length) = _segments[_segment];
                buffer[offset + written++] = hz <= 0 ? 0f : (float)(Math.Sin(_phase) * Envelope(_position, length) * 0.6);
                _phase += 2 * Math.PI * hz / WaveFormat.SampleRate;
                if (++_position >= length)
                {
                    _segment++;
                    _position = 0;
                }
            }
            return written;
        }
    }

    private sealed class AlarmProvider : ISampleProvider
    {
        private double _phase;

        public AlarmProvider(WaveFormat format) => WaveFormat = format;

        public WaveFormat WaveFormat { get; }

        public volatile AlarmPattern Pattern;

        public int Read(float[] buffer, int offset, int count)
        {
            bool on = Pattern == AlarmPattern.Continuous;
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = on ? (float)(Math.Sin(_phase) * 0.5) : 0f;
                _phase += 2 * Math.PI * PiezoHz / SampleRate;
                if (_phase > 2 * Math.PI)
                    _phase -= 2 * Math.PI;
            }
            return count;
        }
    }

    private static double Envelope(int position, int length)
    {
        int fade = Math.Min(220, length / 4);
        if (position < fade)
            return position / (double)fade;
        if (position > length - fade)
            return (length - position) / (double)fade;
        return 1;
    }
}
