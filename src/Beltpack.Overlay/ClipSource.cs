using System.IO;
using System.IO.Compression;

namespace Beltpack.Overlay;

/// <summary>
/// Where the talker recordings come from: talker.pak beside the program (a zip of the clips, as
/// shipped), or a talker folder there (development builds). Clips are named by their path inside,
/// with forward slashes and without ".mp3".
/// </summary>
public abstract class ClipSource : IDisposable
{
    public const string PakName = "talker.pak";
    public static readonly string PakPath = Path.Combine(AppContext.BaseDirectory, PakName);
    public static readonly string FolderPath = Path.Combine(AppContext.BaseDirectory, "talker");

    public abstract string Location { get; }
    public abstract IReadOnlyList<string> Names { get; }

    /// <summary>The clip's MP3 data, or null if there's no such clip.</summary>
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
        if (Directory.Exists(FolderPath) && Directory.EnumerateFiles(FolderPath, "*.mp3", SearchOption.AllDirectories).Any())
            return new Folder(FolderPath);
        problem = $"{PakName} isn't in the program's folder";
        return null;
    }

    private static string? ClipName(string path) =>
        path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? path[..^4].Replace('\\', '/') : null;

    private sealed class Pak : ClipSource
    {
        private readonly ZipArchive _zip;
        private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public Pak(string path)
        {
            Location = path;
            _zip = ZipFile.OpenRead(path);
            foreach (var entry in _zip.Entries)
                if (ClipName(entry.FullName) is { } name)
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

    private sealed class Folder(string path) : ClipSource
    {
        public override string Location { get; } = path;

        public override IReadOnlyList<string> Names { get; } = Directory
            .EnumerateFiles(path, "*.mp3", SearchOption.AllDirectories)
            .Select(f => ClipName(Path.GetRelativePath(path, f))!)
            .Order()
            .ToList();

        public override byte[]? Read(string name)
        {
            string file = Path.Combine(Location, name.Replace('/', Path.DirectorySeparatorChar) + ".mp3");
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        }
    }
}
