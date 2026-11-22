using System.IO;
using System.IO.Compression;

namespace Beltpack.Overlay;

/// <summary>
/// Where the talker recordings come from: talker.pak beside the program (a zip of the clips, as
/// shipped), or a talker folder there (development builds). Clips are named by their path inside,
/// with forward slashes and without the extension (".wav" or ".mp3").
/// </summary>
public abstract class ClipSource : IDisposable
{
    public const string PakName = "talker.pak";
    public static readonly string PakPath = Path.Combine(AppContext.BaseDirectory, PakName);
    public static readonly string FolderPath = Path.Combine(AppContext.BaseDirectory, "talker");

    private static readonly string[] AudioExtensions = [".wav", ".mp3"];

    public abstract string Location { get; }
    public abstract IReadOnlyList<string> Names { get; }

    /// <summary>The clip's audio bytes (WAV or MP3), or null if there's no such clip.</summary>
    public abstract byte[]? Read(string name);

    public virtual void Dispose()
    {
    }

    /// <summary>
    /// Finds the recordings, or returns null with why not: a missing pak, or one that can't be read.
    /// </summary>
    public static ClipSource? Find(out string problem)
    {
        problem = "";
        if (File.Exists(PakPath))
        {
            try
            {
                var pak = new Pak(PakPath);
                if (pak.Names.Count > 0)
                    return pak;
                pak.Dispose();
                problem = $"{PakName} has no recordings in it";
                return null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                problem = $"{PakName} couldn't be read: {ex.Message}";
                return null;
            }
        }
        if (Directory.Exists(FolderPath) && EnumerateAudioFiles(FolderPath).Any())
            return new Folder(FolderPath);
        problem = $"{PakName} isn't in the program's folder";
        return null;
    }

    private static IEnumerable<string> EnumerateAudioFiles(string root) =>
        AudioExtensions.SelectMany(ext => Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories))
            .Where(f => !IsIgnoredPath(Path.GetRelativePath(root, f)));

    /// <summary>Skip backups and editor leftovers under the talker folder.</summary>
    private static bool IsIgnoredPath(string relative) =>
        relative.StartsWith("_Originals" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith("_Originals/", StringComparison.OrdinalIgnoreCase);

    private static string? ClipName(string path)
    {
        foreach (var ext in AudioExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return path[..^ext.Length].Replace('\\', '/');
        }
        return null;
    }

    /// <summary>Prefer .wav over .mp3 when both exist for the same clip name.</summary>
    private static int ExtensionPreference(string path) =>
        path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private sealed class Pak : ClipSource
    {
        private readonly ZipArchive _zip;
        private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public Pak(string path)
        {
            Location = path;
            _zip = ZipFile.OpenRead(path);
            var best = new Dictionary<string, (ZipArchiveEntry Entry, int Pref)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _zip.Entries)
            {
                if (IsIgnoredPath(entry.FullName) || ClipName(entry.FullName) is not { } name)
                    continue;
                int pref = ExtensionPreference(entry.FullName);
                if (!best.TryGetValue(name, out var cur) || pref < cur.Pref)
                    best[name] = (entry, pref);
            }
            foreach (var (name, (entry, _)) in best)
                _entries[name] = entry;
            Names = _entries.Keys.Order().ToList();
        }

        public override string Location { get; }
        public override IReadOnlyList<string> Names { get; }

        // The talker decodes clips on background threads, and a zip can only be read one entry at a time.
        public override byte[]? Read(string name)
        {
            if (!_entries.TryGetValue(name, out var entry))
                return null;
            lock (_zip)
            {
                using var stream = entry.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        public override void Dispose()
        {
            lock (_zip)
                _zip.Dispose();
        }
    }

    private sealed class Folder : ClipSource
    {
        private readonly Dictionary<string, string> _files;

        public Folder(string path)
        {
            Location = path;
            _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var best = new Dictionary<string, (string File, int Pref)>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in EnumerateAudioFiles(path))
            {
                string relative = Path.GetRelativePath(path, file);
                if (ClipName(relative) is not { } name)
                    continue;
                int pref = ExtensionPreference(file);
                if (!best.TryGetValue(name, out var cur) || pref < cur.Pref)
                    best[name] = (file, pref);
            }
            foreach (var (name, (file, _)) in best)
                _files[name] = file;
            Names = _files.Keys.Order().ToList();
        }

        public override string Location { get; }
        public override IReadOnlyList<string> Names { get; }

        public override byte[]? Read(string name) =>
            _files.TryGetValue(name, out var file) ? File.ReadAllBytes(file) : null;
    }
}
