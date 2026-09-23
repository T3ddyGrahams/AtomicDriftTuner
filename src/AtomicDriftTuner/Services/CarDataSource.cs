using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>One unambiguous, read-only physics snapshot. No unpacked files are written to the car or disk.</summary>
public sealed class CarDataSource
{
    private const int MaxFileBytes = 1024 * 1024;
    private const int MaxTotalBytes = 16 * 1024 * 1024;
    private const int MaxArchiveBytes = 64 * 1024 * 1024;
    private static readonly string[] Extensions = [".ini", ".lut", ".rto", ".lua"];
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, byte[]>> Cache = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, byte[]> _files;
    public CarDataEvidence Evidence { get; }
    public string Kind => Evidence.Kind;
    public IEnumerable<string> FileNames => _files.Keys;

    private CarDataSource(IReadOnlyDictionary<string, byte[]> files, CarDataEvidence evidence)
    { _files = files; Evidence = evidence; }

    public static CarDataSource Open(CarProfile car)
    {
        if (string.IsNullOrWhiteSpace(car.SourceFolderPath)) throw new InvalidDataException("Select an installed car to read its physics.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(car.SourceFolderPath));
        if (!string.IsNullOrWhiteSpace(car.SourceFolderName) && !car.SourceFolderName.Trim().Equals(Path.GetFileName(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected car ID and folder disagree. Refresh the installed-car scan before decoding settings.");
        if (!Directory.Exists(root)) throw new InvalidDataException("The selected car folder is unavailable.");
        RejectLink(root);
        var data = Path.Combine(root, "data"); var archive = Path.Combine(root, "data.acd");
        IReadOnlyDictionary<string, byte[]> files;
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matchingUnpackedCopy = false;
        var cameraOnlyDifferences = false;
        string kind;
        if (File.Exists(archive))
        {
            kind = "packed";
            var bytes = ReadBounded(archive, MaxArchiveBytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            hashes.Add("data.acd", hash);
            var id = Path.GetFileName(root);
            var key = id + ":" + hash;
            lock (CacheLock)
            {
                if (!Cache.TryGetValue(key, out files!))
                {
                    files = PackedCarDataReader.Read(bytes, id);
                    // At most four validated archives; nothing is persisted or shared between Windows users.
                    if (Cache.Count >= 4) Cache.Remove(Cache.Keys.First());
                    Cache.Add(key, files);
                }
            }
            if (Directory.Exists(data))
            {
                // CM can leave an exact unpacked copy beside the original archive.
                // Read both independently and require the complete supported file
                // sets and bytes to match, apart from two verified seat-camera values.
                // Never choose precedence or fill gaps in
                // one source with files from the other. Recheck on every Open, even
                // if the packed snapshot came from the cache.
                var unpacked = ReadUnpacked(data, hashes, "data/");
                string? different = null;
                foreach (var name in files.Keys.Union(unpacked.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (!files.TryGetValue(name, out var packedBytes) || !unpacked.TryGetValue(name, out var unpackedBytes)) { different = name; break; }
                    if (packedBytes.AsSpan().SequenceEqual(unpackedBytes)) continue;
                    if (name.Equals("car.ini", StringComparison.OrdinalIgnoreCase) && CarCameraEquivalence.Matches(packedBytes, unpackedBytes))
                    { cameraOnlyDifferences = true; continue; }
                    different = name; break;
                }
                if (different is not null)
                    throw new InvalidDataException($"Both data.acd and data are present, but their supported physics files differ ({different}). " +
                        "ADT cannot verify which copy is active. Use matching copies or restore the intended car version before calculating again.");
                matchingUnpackedCopy = true;
            }
        }
        else if (Directory.Exists(data))
        {
            kind = "unpacked";
            files = ReadUnpacked(data, hashes);
        }
        else throw new InvalidDataException("No data folder or data.acd was found for this car.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("adt/car-data/1\n" + kind + "\n" +
            string.Join("\n", hashes.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value)))));
        return new(files, new() { CarPath = root, Kind = kind, Fingerprint = fingerprint, MatchingUnpackedCopy = matchingUnpackedCopy, CameraOnlyDifferences = cameraOnlyDifferences,
            Fingerprints = new ReadOnlyDictionary<string, string>(hashes) });
    }

    private static IReadOnlyDictionary<string, byte[]> ReadUnpacked(string data, Dictionary<string, string> hashes, string prefix = "")
    {
        RejectLink(data);
        var loaded = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0; var count = 0;
        foreach (var path in Directory.EnumerateFiles(data))
        {
            if (++count > 1024) throw new InvalidDataException("This car data folder has too many files for bounded analysis.");
            var name = Path.GetFileName(path);
            if (!Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) continue;
            ValidateName(name);
            var bytes = ReadBounded(path, MaxFileBytes);
            total += bytes.Length;
            if (total > MaxTotalBytes) throw new InvalidDataException("This car has too much physics text for bounded analysis.");
            if (!loaded.TryAdd(name, bytes)) throw new InvalidDataException("Duplicate car-data filenames cannot be interpreted reliably.");
            hashes.Add(prefix + name.ToLowerInvariant(), Convert.ToHexString(SHA256.HashData(bytes)));
        }
        return new ReadOnlyDictionary<string, byte[]>(loaded);
    }

    public string? ReadText(string name)
    {
        ValidateName(name);
        if (!Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Only local INI, LUT, RTO and Lua text can be inspected. Scripts are never executed.");
        if (!_files.TryGetValue(name, out var bytes)) return null;
        // Some car-author labels use legacy ANSI. Latin-1 preserves those bytes without inventing numeric values.
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new InvalidDataException($"'{name}' is not supported plain text; it may be protected or corrupt.");
        return text.TrimStart('\uFEFF');
    }

    public static void EnsureUnchanged(CarDataEvidence evidence)
    {
        try
        {
            var fresh = Open(new() { SourceFolderPath = evidence.CarPath });
            if (fresh.Evidence.Kind != evidence.Kind || fresh.Evidence.Fingerprint != evidence.Fingerprint)
                throw new InvalidDataException("Car physics changed.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { throw new InvalidDataException("Car physics or its source changed or became unavailable. Reload the baseline and generate/calculate again before saving or staging.", ex); }
    }

    internal static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name != name.Trim() || name.EndsWith('.') ||
            name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 || name is "." or ".." || name.Any(char.IsControl))
            throw new InvalidDataException("A car-data reference is not a safe, flat filename.");
    }
    private static void RejectLink(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked car data is not supported."); }
    private static byte[] ReadBounded(string path, int limit)
    {
        RejectLink(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > limit) throw new InvalidDataException("A car-data source exceeds ADT's bounded reading limit.");
        using var buffer = new MemoryStream(); var chunk = new byte[8192]; int count;
        while ((count = stream.Read(chunk)) > 0)
        { if (buffer.Length + count > limit) throw new InvalidDataException("Car data grew beyond the reading limit."); buffer.Write(chunk, 0, count); }
        return buffer.ToArray();
    }
}
