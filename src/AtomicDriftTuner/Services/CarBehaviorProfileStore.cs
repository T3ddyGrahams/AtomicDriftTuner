using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class CarBehaviorProfileStore
{
    private const string DataDirectoryName =
        "AtomicDriftTuner";

    private const string BehaviorFileName =
        "car-behavior-targets.json";

    private const int MaxBehaviorFileBytes =
        4 * 1024 * 1024;

    private const int MaxBehaviorProfiles =
        4096;

    private const int MaxKeyLength =
        512;

    private const int MaxDisplayNameLength =
        512;

    private static readonly object FileGate =
        new();

    private static readonly JsonSerializerOptions Json =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    private readonly string _directory;
    private readonly string _path;

    public CarBehaviorProfileStore()
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                "Windows did not provide a LocalAppData folder for ADT.");
        }

        _directory =
            Path.GetFullPath(
                Path.Combine(
                    localAppData,
                    DataDirectoryName));

        _path =
            Path.GetFullPath(
                Path.Combine(
                    _directory,
                    BehaviorFileName));

        EnsureDirectory();
    }

    public CarBehaviorTarget Load(
        TuneInput input)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        var key =
            BuildKey(
                input);

        lock (FileGate)
        {
            var all =
                LoadAllUnsafe();

            if (all.TryGetValue(
                    key,
                    out var saved))
            {
                if (saved is null)
                {
                    throw new InvalidDataException(
                        $"ADT behavior storage contains an empty profile for '{key}'.");
                }

                saved.Normalize();

                saved.Key =
                    key;

                if (string.IsNullOrWhiteSpace(
                        saved.DisplayName))
                {
                    saved.DisplayName =
                        BuildDisplayName(
                            input);
                }

                return saved;
            }

            return new CarBehaviorTarget
            {
                Key =
                    key,

                DisplayName =
                    BuildDisplayName(
                        input)
            };
        }
    }

    public void Save(
        TuneInput input,
        CarBehaviorTarget target)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        ArgumentNullException.ThrowIfNull(
            target);

        var key =
            BuildKey(
                input);

        var displayName =
            BuildDisplayName(
                input);

        target.Normalize();

        target.Key =
            key;

        target.DisplayName =
            displayName;

        target.UpdatedUtc =
            DateTime.UtcNow;

        ValidateTarget(
            target,
            expectedKey: key);

        lock (FileGate)
        {
            var all =
                LoadAllUnsafe();

            if (
                !all.ContainsKey(key) &&
                all.Count >= MaxBehaviorProfiles)
            {
                throw new InvalidDataException(
                    $"ADT refused to store more than {MaxBehaviorProfiles:N0} behavior profiles.");
            }

            all[key] =
                target;

            SaveAllUnsafe(
                all);
        }
    }

    public static string BuildKey(
        TuneInput input)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        if (input.DriftPack is null)
        {
            throw new InvalidDataException(
                "ADT cannot build a behavior-profile key because the drift pack is missing.");
        }

        if (input.Car is null)
        {
            throw new InvalidDataException(
                "ADT cannot build a behavior-profile key because the car is missing.");
        }

        var pack =
            NormalizeKeyPart(
                input.DriftPack.Id,
                fallback: "custom-pack");

        string? carSource = null;

        if (!string.IsNullOrWhiteSpace(
                input.Car.SourceFolderName))
        {
            carSource =
                input.Car.SourceFolderName;
        }
        else if (!string.IsNullOrWhiteSpace(
                     input.Car.Id))
        {
            carSource =
                input.Car.Id;
        }
        else if (!string.IsNullOrWhiteSpace(
                     input.Car.DisplayName))
        {
            carSource =
                input.Car.DisplayName;
        }

        var car =
            NormalizeKeyPart(
                carSource,
                fallback: null);

        if (string.IsNullOrWhiteSpace(car))
        {
            throw new InvalidDataException(
                "ADT cannot build a behavior-profile key because the car has no usable identifier.");
        }

        var key =
            $"{pack}|{car}"
                .ToLowerInvariant();

        if (key.Length > MaxKeyLength)
        {
            throw new InvalidDataException(
                $"ADT behavior-profile key exceeds the supported {MaxKeyLength}-character limit.");
        }

        if (ContainsInvalidControlCharacters(
                key))
        {
            throw new InvalidDataException(
                "ADT behavior-profile key contains invalid characters.");
        }

        return key;
    }

    private Dictionary<string, CarBehaviorTarget> LoadAllUnsafe()
    {
        if (!File.Exists(_path))
        {
            return CreateDictionary();
        }

        byte[] bytes;

        try
        {
            var info =
                new FileInfo(
                    _path);

            if (info.Length == 0)
            {
                return CreateDictionary();
            }

            if (info.Length > MaxBehaviorFileBytes)
            {
                throw new InvalidDataException(
                    $"ADT behavior storage exceeds the supported {MaxBehaviorFileBytes:N0}-byte limit.");
            }

            using var stream =
                new FileStream(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    32 * 1024,
                    FileOptions.SequentialScan);

            if (stream.Length == 0)
            {
                return CreateDictionary();
            }

            if (stream.Length > MaxBehaviorFileBytes)
            {
                throw new InvalidDataException(
                    $"ADT behavior storage exceeds the supported {MaxBehaviorFileBytes:N0}-byte limit.");
            }

            bytes =
                new byte[(int)stream.Length];

            var offset =
                0;

            while (offset < bytes.Length)
            {
                var read =
                    stream.Read(
                        bytes,
                        offset,
                        bytes.Length - offset);

                if (read == 0)
                {
                    throw new EndOfStreamException(
                        "ADT behavior storage ended unexpectedly while being read.");
                }

                offset +=
                    read;
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(
                "ADT could not read behavior-profile storage.",
                ex);
        }

        Dictionary<string, CarBehaviorTarget>? loaded;

        try
        {
            loaded =
                JsonSerializer.Deserialize<
                    Dictionary<string, CarBehaviorTarget>>(
                    bytes,
                    Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "ADT behavior-profile storage contains invalid JSON. The original file has been left untouched.",
                ex);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidDataException(
                "ADT behavior-profile storage contains unsupported data. The original file has been left untouched.",
                ex);
        }

        if (loaded is null)
        {
            throw new InvalidDataException(
                "ADT behavior-profile storage is invalid. The original file has been left untouched.");
        }

        if (loaded.Count > MaxBehaviorProfiles)
        {
            throw new InvalidDataException(
                $"ADT behavior storage contains more than the supported {MaxBehaviorProfiles:N0} profiles.");
        }

        var result =
            CreateDictionary();

        foreach (var pair in loaded)
        {
            var storedKey =
                ValidateStoredKey(
                    pair.Key);

            var target =
                pair.Value
                ?? throw new InvalidDataException(
                    $"ADT behavior storage contains an empty profile for '{storedKey}'.");

            target.Normalize();

            target.Key =
                storedKey;

            if (!string.IsNullOrWhiteSpace(
                    target.DisplayName))
            {
                target.DisplayName =
                    NormalizeDisplayName(
                        target.DisplayName);
            }

            ValidateTarget(
                target,
                expectedKey: storedKey);

            if (!result.TryAdd(
                    storedKey,
                    target))
            {
                throw new InvalidDataException(
                    $"ADT behavior storage contains a duplicate profile key: '{storedKey}'.");
            }
        }

        return result;
    }

    private void SaveAllUnsafe(
        Dictionary<string, CarBehaviorTarget> profiles)
    {
        ArgumentNullException.ThrowIfNull(
            profiles);

        if (profiles.Count > MaxBehaviorProfiles)
        {
            throw new InvalidDataException(
                $"ADT refused to store more than {MaxBehaviorProfiles:N0} behavior profiles.");
        }

        foreach (var pair in profiles)
        {
            var key =
                ValidateStoredKey(
                    pair.Key);

            var target =
                pair.Value
                ?? throw new InvalidDataException(
                    $"ADT behavior storage contains an empty profile for '{key}'.");

            ValidateTarget(
                target,
                expectedKey: key);
        }

        string json;

        try
        {
            json =
                JsonSerializer.Serialize(
                    profiles,
                    Json);
        }
        catch (Exception ex)
            when (
                ex is JsonException ||
                ex is NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not serialize behavior-profile storage.",
                ex);
        }

        byte[] bytes;

        try
        {
            bytes =
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
                .GetBytes(
                    json);
        }
        catch (EncoderFallbackException ex)
        {
            throw new InvalidDataException(
                "ADT behavior-profile storage contained invalid text data.",
                ex);
        }

        if (bytes.Length > MaxBehaviorFileBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to save behavior-profile storage larger than {MaxBehaviorFileBytes:N0} bytes.");
        }

        EnsureDirectory();

        WriteAtomically(
            bytes);
    }

    private void WriteAtomically(
        byte[] bytes)
    {
        var temporaryPath =
            Path.Combine(
                _directory,
                $"{BehaviorFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (
                var stream =
                    new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        32 * 1024,
                        FileOptions.WriteThrough))
            {
                stream.Write(
                    bytes,
                    0,
                    bytes.Length);

                stream.Flush(
                    flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                _path,
                overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(
                        temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
                // Temporary-file cleanup must not hide the original save
                // result. ADT never treats *.tmp files as behavior profiles.
            }
        }
    }

    private static void ValidateTarget(
        CarBehaviorTarget target,
        string expectedKey)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        var actualKey =
            ValidateStoredKey(
                target.Key);

        if (!string.Equals(
                actualKey,
                expectedKey,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "ADT behavior-profile data contains a key mismatch.");
        }

        if (!string.IsNullOrWhiteSpace(
                target.DisplayName))
        {
            _ =
                NormalizeDisplayName(
                    target.DisplayName);
        }

        // Do not add numeric/range validation here until the
        // CarBehaviorTarget model itself has been audited.
    }

    private static string BuildDisplayName(
        TuneInput input)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        var packName =
            input.DriftPack?.Name?.Trim();

        var carName =
            input.Car?.DisplayName?.Trim();

        if (string.IsNullOrWhiteSpace(packName))
        {
            packName =
                "Custom Pack";
        }

        if (string.IsNullOrWhiteSpace(carName))
        {
            carName =
                "Unknown Car";
        }

        return NormalizeDisplayName(
            $"{packName} • {carName}");
    }

    private static string? NormalizeKeyPart(
        string? value,
        string? fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized =
            value.Trim();

        if (ContainsInvalidControlCharacters(
                normalized))
        {
            throw new InvalidDataException(
                "ADT behavior-profile identifier contains invalid characters.");
        }

        return normalized;
    }

    private static string ValidateStoredKey(
        string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidDataException(
                "ADT behavior storage contains a profile with no key.");
        }

        var normalized =
            key.Trim()
                .ToLowerInvariant();

        if (normalized.Length > MaxKeyLength)
        {
            throw new InvalidDataException(
                "ADT behavior storage contains an invalid profile key.");
        }

        if (ContainsInvalidControlCharacters(
                normalized))
        {
            throw new InvalidDataException(
                "ADT behavior storage contains an invalid profile key.");
        }

        return normalized;
    }

    private static string NormalizeDisplayName(
        string displayName)
    {
        var normalized =
            displayName.Trim();

        if (normalized.Length > MaxDisplayNameLength)
        {
            throw new InvalidDataException(
                $"ADT behavior-profile display name exceeds the supported {MaxDisplayNameLength}-character limit.");
        }

        if (ContainsInvalidControlCharacters(
                normalized))
        {
            throw new InvalidDataException(
                "ADT behavior-profile display name contains invalid characters.");
        }

        return normalized;
    }

    private static bool ContainsInvalidControlCharacters(
        string value)
    {
        return
            value.Contains('\r') ||
            value.Contains('\n') ||
            value.Contains('\0');
    }

    private static Dictionary<string, CarBehaviorTarget> CreateDictionary()
    {
        return new Dictionary<string, CarBehaviorTarget>(
            StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(
            _directory);
    }
}
