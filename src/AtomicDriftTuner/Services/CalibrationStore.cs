using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class CalibrationStore
{
    private const string DataDirectoryName =
        "AtomicDriftTuner";

    private const string CalibrationFileName =
        "calibrations.json";

    private const int MaxCalibrationFileBytes =
        4 * 1024 * 1024;

    private const int MaxCalibrationCount =
        4096;

    private const int MaxKeyLength =
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

    public CalibrationStore()
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
                    CalibrationFileName));

        EnsureDirectory();
    }

    public CalibrationProfile? Get(
        string key)
    {
        var normalizedKey =
            ValidateKey(
                key);

        lock (FileGate)
        {
            var all =
                LoadAllUnsafe();

            return all.FirstOrDefault(
                x => string.Equals(
                    x.Key,
                    normalizedKey,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Upsert(
        CalibrationProfile calibration)
    {
        ArgumentNullException.ThrowIfNull(
            calibration);

        var normalizedKey =
            ValidateKey(
                calibration.Key);

        lock (FileGate)
        {
            var all =
                LoadAllUnsafe();

            var index =
                all.FindIndex(
                    x => string.Equals(
                        x.Key,
                        normalizedKey,
                        StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                all[index] =
                    calibration;
            }
            else
            {
                if (all.Count >= MaxCalibrationCount)
                {
                    throw new InvalidDataException(
                        $"ADT refused to store more than {MaxCalibrationCount:N0} calibration profiles.");
                }

                all.Add(
                    calibration);
            }

            SaveAllUnsafe(
                all);
        }
    }

    public void Delete(
        string key)
    {
        var normalizedKey =
            ValidateKey(
                key);

        lock (FileGate)
        {
            var all =
                LoadAllUnsafe();

            var removed =
                all.RemoveAll(
                    x => string.Equals(
                        x.Key,
                        normalizedKey,
                        StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
            {
                return;
            }

            SaveAllUnsafe(
                all);
        }
    }

    private List<CalibrationProfile> LoadAllUnsafe()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        byte[] bytes;

        try
        {
            var info =
                new FileInfo(
                    _path);

            if (info.Length == 0)
            {
                return [];
            }

            if (info.Length > MaxCalibrationFileBytes)
            {
                throw new InvalidDataException(
                    $"ADT calibration storage exceeds the supported {MaxCalibrationFileBytes:N0}-byte limit.");
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
                return [];
            }

            if (stream.Length > MaxCalibrationFileBytes)
            {
                throw new InvalidDataException(
                    $"ADT calibration storage exceeds the supported {MaxCalibrationFileBytes:N0}-byte limit.");
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
                        "ADT calibration storage ended unexpectedly while being read.");
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
                "ADT could not read calibration storage.",
                ex);
        }

        List<CalibrationProfile>? calibrations;

        try
        {
            calibrations =
                JsonSerializer.Deserialize<List<CalibrationProfile>>(
                    bytes,
                    Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "ADT calibration storage contains invalid JSON. The original file has been left untouched.",
                ex);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidDataException(
                "ADT calibration storage contains unsupported data. The original file has been left untouched.",
                ex);
        }

        if (calibrations is null)
        {
            throw new InvalidDataException(
                "ADT calibration storage is invalid. The original file has been left untouched.");
        }

        ValidateLoadedCalibrations(
            calibrations);

        return calibrations;
    }

    private void SaveAllUnsafe(
        List<CalibrationProfile> calibrations)
    {
        ArgumentNullException.ThrowIfNull(
            calibrations);

        if (calibrations.Count > MaxCalibrationCount)
        {
            throw new InvalidDataException(
                $"ADT refused to store more than {MaxCalibrationCount:N0} calibration profiles.");
        }

        ValidateLoadedCalibrations(
            calibrations);

        string json;

        try
        {
            json =
                JsonSerializer.Serialize(
                    calibrations,
                    Json);
        }
        catch (Exception ex)
            when (
                ex is JsonException ||
                ex is NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not serialize calibration storage.",
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
                "ADT calibration storage contained invalid text data.",
                ex);
        }

        if (bytes.Length > MaxCalibrationFileBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to save calibration storage larger than {MaxCalibrationFileBytes:N0} bytes.");
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
                $"{CalibrationFileName}.{Guid.NewGuid():N}.tmp");

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
                if (File.Exists(temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
                // Temporary-file cleanup must not hide the original save
                // result. ADT never loads *.tmp files as calibration data.
            }
        }
    }

    private static void ValidateLoadedCalibrations(
        List<CalibrationProfile> calibrations)
    {
        if (calibrations.Count > MaxCalibrationCount)
        {
            throw new InvalidDataException(
                $"ADT calibration storage contains more than the supported {MaxCalibrationCount:N0} profiles.");
        }

        var seenKeys =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var calibration in calibrations)
        {
            if (calibration is null)
            {
                throw new InvalidDataException(
                    "ADT calibration storage contains an empty calibration entry.");
            }

            var key =
                ValidateStoredKey(
                    calibration.Key);

            if (!seenKeys.Add(key))
            {
                throw new InvalidDataException(
                    $"ADT calibration storage contains a duplicate calibration key: '{key}'.");
            }
        }
    }

    private static string ValidateKey(
        string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException(
                "Calibration key is required.",
                nameof(key));
        }

        var normalized =
            key.Trim();

        if (normalized.Length > MaxKeyLength)
        {
            throw new ArgumentException(
                $"Calibration key exceeds the supported {MaxKeyLength}-character limit.",
                nameof(key));
        }

        if (ContainsInvalidControlCharacters(normalized))
        {
            throw new ArgumentException(
                "Calibration key contains invalid characters.",
                nameof(key));
        }

        return normalized;
    }

    private static string ValidateStoredKey(
        string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidDataException(
                "ADT calibration storage contains a calibration with no key.");
        }

        var normalized =
            key.Trim();

        if (normalized.Length > MaxKeyLength)
        {
            throw new InvalidDataException(
                "ADT calibration storage contains an invalid calibration key.");
        }

        if (ContainsInvalidControlCharacters(normalized))
        {
            throw new InvalidDataException(
                "ADT calibration storage contains an invalid calibration key.");
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

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(
            _directory);
    }
}
