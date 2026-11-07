using System.Collections.Concurrent;
using System.IO;
using System.Speech.Synthesis;
using Beltpack.Core.Talk;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Beltpack.Overlay;

/// <summary>
/// The Beltpack "talker", which read status messages out over the crew's portable radios. Plays the
/// recorded clips from talker.pak beside the program, staying silent for anything not recorded.
/// Without the recordings it falls back to a Windows speech voice.
/// </summary>
public sealed class Talker : IDisposable
{
    private readonly ClipVoice? _clips;
    private readonly SpeechSynthesizer? _synth;
    private int _volume;

    public Talker(int volumePercent)
    {
        _volume = volumePercent;
        if (ClipSource.Find(out string problem) is { } source)
        {
            _clips = new ClipVoice(source, Gain(volumePercent));
            return;
        }
        RecordingsProblem = problem;
        try
        {
            _synth = new SpeechSynthesizer { Volume = (int)(Gain(volumePercent) * 100), Rate = 1 };
            _synth.SetOutputToDefaultAudioDevice();
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException)
        {
            _synth = null;
        }
    }

    public bool Enabled { get; set; } = true;

    /// <summary>Percent of the loudest setting.</summary>
    public int Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            if (_clips is not null)
                _clips.Volume = Gain(value);
            if (_synth is not null)
                _synth.Volume = (int)(Gain(value) * 100);
        }
    }

    // The recordings are loud, so 100% is well under full scale.
    private const float FullGain = 0.42f;

    private static float Gain(int percent) => Math.Clamp(percent, 0, 100) / 100f * FullGain;
    public bool UsesRecordings => _clips is not null;

    /// <summary>Why the recordings aren't being used, or empty if they are.</summary>
    public string RecordingsProblem { get; } = "";

    /// <summary>Raised with each part of a message that has no recording.</summary>
    public event Action<string>? Missing;

    /// <summary>Raised when a recording can't be loaded or played.</summary>
    public event Action<string>? Failed
    {
        add { if (_clips is not null) _clips.Failed += value; }
        remove { if (_clips is not null) _clips.Failed -= value; }
    }

    public ClipVoice? Clips => _clips;

    public void Say(string text)
    {
        if (!Enabled)
            return;
        if (_clips is not null)
        {
            var (clips, missing) = TalkerScript.Parse(text);
            foreach (var m in missing)
                Missing?.Invoke(m);
            _clips.Play(clips);
            return;
        }
        if (_synth is null)
            return;
        _synth.SpeakAsyncCancelAll();
        _synth.SpeakAsync(text);
    }

    public void Dispose()
    {
        _clips?.Dispose();
        _synth?.Dispose();
    }

    /// <summary>Strings clips together, trimmed of their leading and trailing silence.</summary>
    public sealed class ClipVoice : IDisposable
    {
        private const int SampleRate = 44100;
        private const double WordGap = 0.06;
        private const double SentenceGap = 0.35;

        private readonly ClipSource _source;
        private readonly ConcurrentDictionary<string, float[]?> _cache = new();
        private readonly WaveOutEvent? _output;
        private readonly QueueProvider _queue = new(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1));
        private readonly VolumeSampleProvider _volume;
        private int _version;
        private static int _decoded;

        public ClipVoice(ClipSource source, float volume, bool output = true)
        {
            _source = source;
            _volume = new VolumeSampleProvider(_queue) { Volume = volume };
            if (!output)
                return;
            _output = new WaveOutEvent { DesiredLatency = 120 };
            _output.PlaybackStopped += (_, e) =>
            {
                if (e.Exception is not null)
                    Failed?.Invoke($"playback stopped: {e.Exception}");
            };
            _output.Init(_volume);
            _output.Play();
        }

        public float Volume
        {
            get => _volume.Volume;
            set => _volume.Volume = value;
        }

        /// <summary>Replaces whatever is playing, like a new message cutting off the last one.</summary>
        public void Play(IReadOnlyList<TalkerClip> clips)
        {
            int version = Interlocked.Increment(ref _version);
            Task.Run(() =>
            {
                try
                {
                    var samples = Render(clips);
                    if (version == Volatile.Read(ref _version))
                        _queue.Replace(samples);
                    Queued?.Invoke(samples.Length);
                }
                catch (Exception ex)
                {
                    Failed?.Invoke(ex.ToString());
                }
            });
        }

        public event Action<int>? Queued;
        public event Action<string>? Failed;

        public float[] Render(IReadOnlyList<TalkerClip> clips)
        {
            var samples = new List<float>();
            foreach (var clip in clips)
            {
                if (Load(clip.Name) is not { } audio)
                    continue;
                samples.AddRange(audio);
                samples.AddRange(new float[(int)((clip.EndsSentence ? SentenceGap : WordGap) * SampleRate)]);
            }
            return samples.ToArray();
        }

        private float[]? Load(string name) => _cache.GetOrAdd(name, n =>
        {
            bool first = Interlocked.Exchange(ref _decoded, 1) == 0;
            if (first)
                CrashLog.Step($"talker: decoding the first recording ({n})");
            try
            {
                if (_source.Read(n) is not { } mp3)
                    return null;
                using var reader = new Mp3FileReader(new MemoryStream(mp3));
                ISampleProvider source = reader.ToSampleProvider();
                if (source.WaveFormat.Channels == 2)
                    source = source.ToMono();
                if (source.WaveFormat.SampleRate != SampleRate)
                    source = new WdlResamplingSampleProvider(source, SampleRate);
                var all = new List<float>();
                var buffer = new float[SampleRate];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    all.AddRange(buffer.AsSpan(0, read).ToArray());
                if (first)
                    CrashLog.Step("talker: first recording decoded");
                return Trim(all.ToArray());
            }
            catch (Exception ex)
            {
                Failed?.Invoke($"{n}: {ex}");
                return null;
            }
        });

        /// <summary>Cuts the silence the clips are padded with, keeping a little either side.</summary>
        public static float[] Trim(float[] audio)
        {
            float peak = 0;
            foreach (var v in audio)
                peak = Math.Max(peak, Math.Abs(v));
            if (peak == 0)
                return audio;
            float threshold = peak * 0.03f;
            int first = Array.FindIndex(audio, v => Math.Abs(v) > threshold);
            int last = Array.FindLastIndex(audio, v => Math.Abs(v) > threshold);
            first = Math.Max(0, first - SampleRate / 60);
            last = Math.Min(audio.Length - 1, last + SampleRate / 20);
            return audio[first..(last + 1)];
        }

        public IReadOnlyList<string> Names => _source.Names;

        public void Dispose()
        {
            _output?.Stop();
            _output?.Dispose();
            _source.Dispose();
        }

        private sealed class QueueProvider(WaveFormat format) : ISampleProvider
        {
            private readonly object _lock = new();
            private float[] _samples = [];
            private int _position;

            public WaveFormat WaveFormat { get; } = format;

            public void Replace(float[] samples)
            {
                lock (_lock)
                {
                    _samples = samples;
                    _position = 0;
                }
            }

            public int Read(float[] buffer, int offset, int count)
            {
                // NAudio passes a byte buffer seen as floats, which Array.Copy and Array.Clear
                // reject, so the samples are written one at a time.
                lock (_lock)
                {
                    for (int i = 0; i < count; i++)
                        buffer[offset + i] = _position < _samples.Length ? _samples[_position++] : 0f;
                }
                return count;
            }
        }
    }
}
