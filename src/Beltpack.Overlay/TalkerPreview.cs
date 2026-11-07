using System.IO;
using Beltpack.Core.Talk;
using NAudio.Wave;

namespace Beltpack.Overlay;

/// <summary>
/// Development aid: "Beltpack.Overlay.exe --render-talker &lt;folder&gt;" writes a few talker messages,
/// as the recorded clips would play them, to WAV files, with a list of what has no recording.
/// </summary>
public static class TalkerPreview
{
    private static readonly string[] Messages =
    [
        "Locomotive in remote mode.",
        "Operator requested emergency. Recover emergency from beltpack.",
        "Reset safety control timeout. Recover train brake from beltpack.",
        "B N S F 4 0 2 6, O C U A. Low brake pipe pressure. Brake pipe 52 P S I.",
        "Locomotive 1 2 3 4, O C U B. Recover service from beltpack. Cause: Communication interrupted.",
    ];

    /// <summary>"--say &lt;text&gt; &lt;log file&gt;": speaks one message through the live talker and logs what happened.</summary>
    public static void Say(string text, string logFile)
    {
        var log = new List<string>();
        using var talker = new Talker(50);
        log.Add($"recordings: {talker.UsesRecordings}");
        talker.Missing += m => { lock (log) log.Add($"missing: {m}"); };
        talker.Failed += m => { lock (log) log.Add($"failed: {m}"); };
        if (talker.Clips is { } clips)
            clips.Queued += n => { lock (log) log.Add($"queued {n} samples ({n / 44100.0:0.0} s)"); };
        float peak = 0;
        using var capture = new WasapiLoopbackCapture();
        capture.DataAvailable += (_, e) =>
        {
            var floats = new WaveBuffer(e.Buffer);
            for (int i = 0; i < e.BytesRecorded / 4; i++)
                peak = Math.Max(peak, Math.Abs(floats.FloatBuffer[i]));
        };
        capture.StartRecording();
        talker.Say(text);
        Thread.Sleep(8000);
        capture.StopRecording();
        log.Add($"loudest sound reaching the speakers: {peak:0.000}");
        lock (log)
            File.WriteAllLines(logFile, log);
    }

    public static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        var source = ClipSource.Find(out string problem) ?? throw new FileNotFoundException(problem);
        using var voice = new Talker.ClipVoice(source, 1, output: false);
        var report = new List<string> { $"Clips from {source.Location}" };
        for (int i = 0; i < Messages.Length; i++)
        {
            var (clips, missing) = TalkerScript.Parse(Messages[i]);
            var samples = voice.Render(clips);
            string file = Path.Combine(folder, $"{i + 1}.wav");
            using (var writer = new WaveFileWriter(file, new WaveFormat(44100, 16, 1)))
                writer.WriteSamples(samples, 0, samples.Length);
            report.Add($"{i + 1}.wav  {samples.Length / 44100.0:0.0} s  \"{Messages[i]}\"");
            report.Add($"    clips: {string.Join(", ", clips.Select(c => c.Name))}");
            if (missing.Count > 0)
                report.Add($"    no recording: {string.Join(" | ", missing)}");
        }
        report.Add("");
        report.Add("Clip lengths, as recorded and trimmed:");
        foreach (var name in source.Names)
        {
            using var reader = new Mp3FileReader(new MemoryStream(source.Read(name)!));
            double trimmed = voice.Render([new TalkerClip(name, false)]).Length / 44100.0 - 0.06;
            report.Add($"    {name,-40} {reader.TotalTime.TotalSeconds,5:0.00} s  {trimmed,5:0.00} s");
        }
        File.WriteAllLines(Path.Combine(folder, "report.txt"), report);
    }
}
