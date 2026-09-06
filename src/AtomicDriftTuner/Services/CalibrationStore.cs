using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Persistent per-context ADT calibration storage.
///
/// The on-disk format intentionally remains the existing JSON array of
/// CalibrationProfile objects so current beta data stays compatible. This
/// store validates every profile, serializes read/modify/write operations
/// across ADT processes, keeps a last-known-good backup, and verifies staged
/// and published writes before returning success.
/// </summary>
public sealed class CalibrationStore
{
    private const string DataDirectoryName =
        "AtomicDriftTuner";

    private const string CalibrationFileName =
        "calibrations.json";

    private const string BackupFileName =
        "calibrations.backup.json";

    private const int MaxCalibrationFileBytes =
        4 * 1024 * 1024;

    private const int MaxRecoverySnapshotBytes =
        8 * 1024 * 1024;

    private const int MaxCalibrationCount =
        4096;

    private const int MaxKeyLength =
        512;

    private const int MaxQuarantineFiles =
        3;

    private const int MinimumTorqueLimitDelta =
        -20;

    private const int MaximumTorqueLimitDelta =
        20;

    private const int MinimumWheelSpeedDelta =
        -40;

    private const int MaximumWheelSpeedDelta =
        40;

    private const int MinimumDampingDelta =
        -15;

    private const int MaximumDampingDelta =
        20;

    private const int MinimumFrictionDelta =
        -10;

    private const int MaximumFrictionDelta =
        12;

    private const int MinimumSpeedDampingDelta =
        -10;

    private const int MaximumSpeedDampingDelta =
        20;

    private const int MinimumInterpolationDelta =
        -3;

    private const int MaximumInterpolationDelta =
        4;

    private const int MinimumAcGainDelta =
        -12;

    private const int MaximumAcGainDelta =
        12;

    private static readonly TimeSpan MaximumFutureClockSkew =
        TimeSpan.FromMinutes(
            5);

    private static readonly TimeSpan InterprocessLockTimeout =
        TimeSpan.FromSeconds(
            5);

    private static readonly object FileGate =
        new();

    private static readonly JsonSerializerOptions Json =
        new()
        {
            WriteIndented =
                true,

            PropertyNameCaseInsensitive =
                true,

            AllowTrailingCommas =
                true,

            ReadCommentHandling =
                JsonCommentHandling.Skip,

            MaxDepth =
                64
        };

    private static readonly JsonDocumentOptions DocumentOptions =
        new()
        {
            AllowTrailingCommas =
                true,

            CommentHandling =
                JsonCommentHandling.Skip,

            MaxDepth =
                64
        };

    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier:
                false,
            throwOnInvalidBytes:
                true);

    private readonly string _directory;
    private readonly string _path;
    private readonly string _backupPath;

    public CalibrationStore()
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(
                localAppData))
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

        _backupPath =
            Path.GetFullPath(
                Path.Combine(
                    _directory,
                    BackupFileName));
    }

    public CalibrationProfile? Get(
        string key)
    {
        var normalizedKey =
            NormalizeLookupKey(
                key);

        lock (FileGate)
        {
            using var lease =
                AcquireInterprocessLock();

            var all =
                LoadAllUnsafe();

            var found =
                all.FirstOrDefault(
                    x => string.Equals(
                        x.Key,
                        normalizedKey,
                        StringComparison.OrdinalIgnoreCase));

            return found is null
                ? null
                : Clone(
                    found);
        }
    }

    public void Upsert(
        CalibrationProfile calibration)
    {
        ArgumentNullException.ThrowIfNull(
            calibration);

        // Snapshot caller-owned mutable state before touching persistent data.
        var candidate =
            NormalizeAndValidateProfile(
                calibration);

        lock (FileGate)
        {
            using var lease =
                AcquireInterprocessLock();

            var all =
                LoadAllUnsafe();

            var index =
                all.FindIndex(
                    x => string.Equals(
                        x.Key,
                        candidate.Key,
                        StringComparison.OrdinalIgnoreCase));

            if (index >=
                0)
            {
                all[index] =
                    candidate;
            }
            else
            {
                if (all.Count >=
                    MaxCalibrationCount)
                {
                    throw new InvalidDataException(
                        $"ADT refused to store more than {MaxCalibrationCount:N0} calibration profiles.");
                }

                all.Add(
                    candidate);
            }

            SaveAllUnsafe(
                all);
        }
    }

    public void Delete(
        string key)
    {
        var normalizedKey =
            NormalizeLookupKey(
                key);

        lock (FileGate)
        {
            using var lease =
                AcquireInterprocessLock();

            var all =
                LoadAllUnsafe();

            var removed =
                all.RemoveAll(
                    x => string.Equals(
                        x.Key,
                        normalizedKey,
                        StringComparison.OrdinalIgnoreCase));

            if (removed ==
                0)
            {
                return;
            }

            SaveAllUnsafe(
                all);
        }
    }

    private List<CalibrationProfile> LoadAllUnsafe()
    {
        if (!File.Exists(
                _path))
        {
            return [];
        }

        Exception primaryFailure;

        try
        {
            return ReadValidatedCalibrationFile(
                _path,
                out _);
        }
        catch (Exception ex)
            when (
                IsRecoverableStorageException(
                    ex))
        {
            primaryFailure =
                ex;
        }

        if (File.Exists(
                _backupPath))
        {
            try
            {
                return ReadValidatedCalibrationFile(
                    _backupPath,
                    out _);
            }
            catch (Exception backupFailure)
                when (
                    IsRecoverableStorageException(
                        backupFailure))
            {
                throw new InvalidDataException(
                    "ADT calibration storage is damaged or unreadable, and the last-known-good backup is also unavailable. The original files have been left untouched.",
                    new AggregateException(
                        primaryFailure,
                        backupFailure));
            }
        }

        throw new InvalidDataException(
            "ADT calibration storage is damaged or unreadable and no valid backup is available. The original file has been left untouched.",
            primaryFailure);
    }

    private void SaveAllUnsafe(
        List<CalibrationProfile> calibrations)
    {
        ArgumentNullException.ThrowIfNull(
            calibrations);

        var normalized =
            NormalizeAndValidateCalibrations(
                calibrations);

        var bytes =
            SerializeCalibrations(
                normalized);

        EnsureSafeDirectoryForWrite();

        var previousSnapshot =
            PrepareExistingPrimaryForSave();

        PublishPrimaryVerified(
            bytes,
            previousSnapshot);
    }

    private List<CalibrationProfile> ReadValidatedCalibrationFile(
        string path,
        out byte[] rawBytes)
    {
        EnsureExistingFileIsRegular(
            path);

        rawBytes =
            ReadAllBytesBounded(
                path,
                MaxCalibrationFileBytes);

        ValidateRawEnvelope(
            rawBytes);

        List<CalibrationProfile>? calibrations;

        try
        {
            calibrations =
                JsonSerializer.Deserialize<List<CalibrationProfile>>(
                    rawBytes,
                    Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "ADT calibration storage contains invalid JSON.",
                ex);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidDataException(
                "ADT calibration storage contains unsupported data.",
                ex);
        }

        if (calibrations is null)
        {
            throw new InvalidDataException(
                "ADT calibration storage is invalid.");
        }

        return NormalizeAndValidateCalibrations(
            calibrations);
    }

    private static byte[] SerializeCalibrations(
        List<CalibrationProfile> calibrations)
    {
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
                ex is JsonException or
                NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not serialize calibration storage.",
                ex);
        }

        byte[] bytes;

        try
        {
            bytes =
                StrictUtf8.GetBytes(
                    json);
        }
        catch (EncoderFallbackException ex)
        {
            throw new InvalidDataException(
                "ADT calibration storage contained invalid text data.",
                ex);
        }

        if (
            bytes.Length <=
                0 ||
            bytes.Length >
                MaxCalibrationFileBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to save calibration storage outside the supported 1..{MaxCalibrationFileBytes:N0}-byte size.");
        }

        // Validate the exact representation that will be staged.
        ValidateRawEnvelope(
            bytes);

        return
            bytes;
    }

    private static void ValidateRawEnvelope(
        byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(
            bytes);

        if (
            bytes.Length <=
                0 ||
            bytes.Length >
                MaxCalibrationFileBytes)
        {
            throw new InvalidDataException(
                "ADT calibration storage has an invalid file size.");
        }

        try
        {
            _ =
                StrictUtf8.GetString(
                    bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                "ADT calibration storage is not valid UTF-8.",
                ex);
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    bytes,
                    DocumentOptions);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "ADT calibration storage root must be a JSON array.");
            }

            if (document.RootElement.GetArrayLength() >
                MaxCalibrationCount)
            {
                throw new InvalidDataException(
                    $"ADT calibration storage contains more than the supported {MaxCalibrationCount:N0} profiles.");
            }

            foreach (var item in
                     document.RootElement.EnumerateArray())
            {
                if (item.ValueKind !=
                    JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        "ADT calibration storage contains a non-object calibration entry.");
                }

                // Key is the only field that must physically exist for
                // compatibility with older beta records. Missing newer numeric
                // members safely deserialize to neutral zero values.
                if (
                    !TryGetPropertyIgnoreCase(
                        item,
                        "Key",
                        out var keyElement) ||
                    keyElement.ValueKind !=
                        JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(
                        keyElement.GetString()))
                {
                    throw new InvalidDataException(
                        "ADT calibration storage contains a calibration entry without a valid Key field.");
                }
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "ADT calibration storage contains invalid JSON.",
                ex);
        }
    }

    private static List<CalibrationProfile> NormalizeAndValidateCalibrations(
        IEnumerable<CalibrationProfile> calibrations)
    {
        ArgumentNullException.ThrowIfNull(
            calibrations);

        var result =
            new List<CalibrationProfile>();

        var seenKeys =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var calibration in
                 calibrations)
        {
            if (calibration is null)
            {
                throw new InvalidDataException(
                    "ADT calibration storage contains an empty calibration entry.");
            }

            if (result.Count >=
                MaxCalibrationCount)
            {
                throw new InvalidDataException(
                    $"ADT calibration storage contains more than the supported {MaxCalibrationCount:N0} profiles.");
            }

            var normalized =
                NormalizeAndValidateProfile(
                    calibration);

            if (!seenKeys.Add(
                    normalized.Key))
            {
                throw new InvalidDataException(
                    $"ADT calibration storage contains a duplicate calibration key: '{normalized.Key}'.");
            }

            result.Add(
                normalized);
        }

        return
            result;
    }

    private static CalibrationProfile NormalizeAndValidateProfile(
        CalibrationProfile calibration)
    {
        ArgumentNullException.ThrowIfNull(
            calibration);

        var key =
            NormalizeStoredKey(
                calibration.Key);

        if (calibration.Samples <
            0)
        {
            throw new InvalidDataException(
                "ADT calibration storage contains a negative sample count.");
        }

        ValidateTimestamp(
            calibration.UpdatedUtc);

        ValidateRange(
            calibration.TorqueLimitDelta,
            MinimumTorqueLimitDelta,
            MaximumTorqueLimitDelta,
            "base torque delta");

        ValidateRange(
            calibration.WheelSpeedDelta,
            MinimumWheelSpeedDelta,
            MaximumWheelSpeedDelta,
            "wheel speed delta");

        ValidateRange(
            calibration.DampingDelta,
            MinimumDampingDelta,
            MaximumDampingDelta,
            "wheel damper delta");

        ValidateRange(
            calibration.FrictionDelta,
            MinimumFrictionDelta,
            MaximumFrictionDelta,
            "wheel friction delta");

        ValidateRange(
            calibration.SpeedDampingDelta,
            MinimumSpeedDampingDelta,
            MaximumSpeedDampingDelta,
            "high-speed damping delta");

        ValidateRange(
            calibration.InterpolationDelta,
            MinimumInterpolationDelta,
            MaximumInterpolationDelta,
            "interpolation delta");

        ValidateRange(
            calibration.AcGainDelta,
            MinimumAcGainDelta,
            MaximumAcGainDelta,
            "Assetto Corsa gain delta");

        return new CalibrationProfile
        {
            Key =
                key,

            Samples =
                calibration.Samples,

            UpdatedUtc =
                NormalizeUtc(
                    calibration.UpdatedUtc),

            TorqueLimitDelta =
                calibration.TorqueLimitDelta,

            WheelSpeedDelta =
                calibration.WheelSpeedDelta,

            DampingDelta =
                calibration.DampingDelta,

            FrictionDelta =
                calibration.FrictionDelta,

            SpeedDampingDelta =
                calibration.SpeedDampingDelta,

            InterpolationDelta =
                calibration.InterpolationDelta,

            AcGainDelta =
                calibration.AcGainDelta
        };
    }

    private static void ValidateTimestamp(
        DateTime value)
    {
        var utc =
            NormalizeUtc(
                value);

        if (utc >
            DateTime.UtcNow +
            MaximumFutureClockSkew)
        {
            throw new InvalidDataException(
                "ADT calibration storage contains a timestamp too far in the future.");
        }
    }

    private static DateTime NormalizeUtc(
        DateTime value)
    {
        if (value ==
            default)
        {
            throw new InvalidDataException(
                "ADT calibration storage contains an invalid timestamp.");
        }

        try
        {
            return value.Kind switch
            {
                DateTimeKind.Utc =>
                    value,

                DateTimeKind.Local =>
                    value.ToUniversalTime(),

                DateTimeKind.Unspecified =>
                    throw new InvalidDataException(
                        "ADT calibration timestamps must include UTC/local time semantics."),

                _ =>
                    throw new InvalidDataException(
                        "ADT calibration storage contains an invalid timestamp kind.")
            };
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "ADT calibration storage contains an invalid timestamp.",
                ex);
        }
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string description)
    {
        if (
            value <
                minimum ||
            value >
                maximum)
        {
            throw new InvalidDataException(
                $"ADT calibration storage contains {description}={value}, outside the supported {minimum}..{maximum} range.");
        }
    }

    private static string NormalizeLookupKey(
        string key)
    {
        if (string.IsNullOrWhiteSpace(
                key))
        {
            throw new ArgumentException(
                "Calibration key is required.",
                nameof(key));
        }

        try
        {
            return NormalizeKeyCore(
                key);
        }
        catch (InvalidDataException ex)
        {
            throw new ArgumentException(
                ex.Message,
                nameof(key),
                ex);
        }
    }

    private static string NormalizeStoredKey(
        string? key)
    {
        if (string.IsNullOrWhiteSpace(
                key))
        {
            throw new InvalidDataException(
                "ADT calibration storage contains a calibration with no key.");
        }

        return NormalizeKeyCore(
            key);
    }

    private static string NormalizeKeyCore(
        string key)
    {
        var normalized =
            key.Trim();

        if (
            normalized.Length <=
                0 ||
            normalized.Length >
                MaxKeyLength)
        {
            throw new InvalidDataException(
                $"ADT calibration key must contain 1..{MaxKeyLength} characters.");
        }

        if (ContainsInvalidControlCharacters(
                normalized))
        {
            throw new InvalidDataException(
                "ADT calibration key contains invalid control characters.");
        }

        var parts =
            normalized.Split(
                '|');

        if (parts.Length !=
            4)
        {
            throw new InvalidDataException(
                "ADT calibration key must identify exactly wheelbase | wheel | drift pack | car.");
        }

        for (var i =
                 0;
             i <
                 parts.Length;
             i++)
        {
            var part =
                parts[i].Trim();

            if (string.IsNullOrWhiteSpace(
                    part))
            {
                throw new InvalidDataException(
                    "ADT calibration key contains an empty identity component.");
            }

            if (ContainsInvalidControlCharacters(
                    part))
            {
                throw new InvalidDataException(
                    "ADT calibration key contains invalid characters.");
            }

            parts[i] =
                part;
        }

        return string.Join(
                "|",
                parts)
            .ToLowerInvariant();
    }

    private static bool ContainsInvalidControlCharacters(
        string value)
    {
        foreach (var character in
                 value)
        {
            if (char.IsControl(
                    character))
            {
                return true;
            }
        }

        return false;
    }

    private ExistingPrimarySnapshot? PrepareExistingPrimaryForSave()
    {
        if (!File.Exists(
                _path))
        {
            return null;
        }

        EnsureExistingFileIsRegular(
            _path);

        byte[] rawSnapshot;

        try
        {
            rawSnapshot =
                ReadAllBytesBounded(
                    _path,
                    MaxRecoverySnapshotBytes);
        }
        catch (Exception ex)
            when (
                IsRecoverableStorageException(
                    ex))
        {
            throw new IOException(
                "ADT could not safely snapshot the existing calibration file before replacing it.",
                ex);
        }

        var snapshot =
            new ExistingPrimarySnapshot(
                rawSnapshot);

        try
        {
            _ =
                ReadValidatedCalibrationFile(
                    _path,
                    out var validBytes);

            // Refresh the rollback copy only from a currently valid primary.
            WriteBackupVerified(
                validBytes);
        }
        catch (Exception ex)
            when (
                IsRecoverableStorageException(
                    ex))
        {
            // Never replace a known-good backup with corrupt primary bytes.
            // Preserve a readable copy of the damaged primary for diagnostics
            // before the next successful save repairs the canonical file.
            QuarantineInvalidPrimaryBestEffort(
                rawSnapshot);
        }

        return
            snapshot;
    }

    private void WriteBackupVerified(
        byte[] bytes)
    {
        var temporaryPath =
            Path.Combine(
                _directory,
                $"{BackupFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            WriteBytesToNewFile(
                temporaryPath,
                bytes);

            var staged =
                ReadValidatedCalibrationFile(
                    temporaryPath,
                    out var stagedBytes);

            _ =
                staged;

            VerifySameBytes(
                bytes,
                stagedBytes,
                "ADT calibration backup staging verification failed.");

            File.Move(
                temporaryPath,
                _backupPath,
                overwrite:
                    true);

            _ =
                ReadValidatedCalibrationFile(
                    _backupPath,
                    out var publishedBytes);

            VerifySameBytes(
                bytes,
                publishedBytes,
                "ADT calibration backup verification failed after publishing.");
        }
        finally
        {
            TryDeleteFile(
                temporaryPath);
        }
    }

    private void PublishPrimaryVerified(
        byte[] bytes,
        ExistingPrimarySnapshot? previousSnapshot)
    {
        var temporaryPath =
            Path.Combine(
                _directory,
                $"{CalibrationFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            WriteBytesToNewFile(
                temporaryPath,
                bytes);

            _ =
                ReadValidatedCalibrationFile(
                    temporaryPath,
                    out var stagedBytes);

            VerifySameBytes(
                bytes,
                stagedBytes,
                "ADT calibration staging verification failed.");

            File.Move(
                temporaryPath,
                _path,
                overwrite:
                    true);

            try
            {
                _ =
                    ReadValidatedCalibrationFile(
                        _path,
                        out var publishedBytes);

                VerifySameBytes(
                    bytes,
                    publishedBytes,
                    "ADT calibration verification failed after publishing.");
            }
            catch
            {
                RestorePreviousPrimaryBestEffort(
                    previousSnapshot);

                throw;
            }
        }
        finally
        {
            TryDeleteFile(
                temporaryPath);
        }
    }

    private static void VerifySameBytes(
        byte[] expected,
        byte[] actual,
        string errorMessage)
    {
        var expectedHash =
            SHA256.HashData(
                expected);

        var actualHash =
            SHA256.HashData(
                actual);

        if (!CryptographicOperations.FixedTimeEquals(
                expectedHash,
                actualHash))
        {
            throw new IOException(
                errorMessage);
        }
    }

    private static void WriteBytesToNewFile(
        string path,
        byte[] bytes)
    {
        using var stream =
            new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                32 * 1024,
                FileOptions.WriteThrough);

        stream.Write(
            bytes,
            0,
            bytes.Length);

        stream.Flush(
            flushToDisk:
                true);
    }

    private void RestorePreviousPrimaryBestEffort(
        ExistingPrimarySnapshot? previousSnapshot)
    {
        try
        {
            if (previousSnapshot is null)
            {
                TryDeleteFile(
                    _path);

                return;
            }

            var restorePath =
                Path.Combine(
                    _directory,
                    $"{CalibrationFileName}.{Guid.NewGuid():N}.restore.tmp");

            try
            {
                WriteBytesToNewFile(
                    restorePath,
                    previousSnapshot.Bytes);

                File.Move(
                    restorePath,
                    _path,
                    overwrite:
                        true);
            }
            finally
            {
                TryDeleteFile(
                    restorePath);
            }
        }
        catch
        {
            // Best-effort recovery only. Never hide the original save failure.
        }
    }

    private void QuarantineInvalidPrimaryBestEffort(
        byte[] rawBytes)
    {
        try
        {
            var timestamp =
                DateTime.UtcNow.ToString(
                    "yyyyMMdd-HHmmss-fff");

            var quarantinePath =
                Path.Combine(
                    _directory,
                    $"calibrations.corrupt-{timestamp}.json");

            using (
                var stream =
                    new FileStream(
                        quarantinePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        32 * 1024,
                        FileOptions.WriteThrough))
            {
                stream.Write(
                    rawBytes,
                    0,
                    rawBytes.Length);

                stream.Flush(
                    flushToDisk:
                        true);
            }

            CleanupOldQuarantineFilesBestEffort();
        }
        catch
        {
            // Diagnostic preservation is best-effort and must not block repair.
        }
    }

    private void CleanupOldQuarantineFilesBestEffort()
    {
        try
        {
            if (!Directory.Exists(
                    _directory))
            {
                return;
            }

            var files =
                Directory
                    .EnumerateFiles(
                        _directory,
                        "calibrations.corrupt-*.json",
                        SearchOption.TopDirectoryOnly)
                    .Select(
                        path =>
                            new FileInfo(
                                path))
                    .Where(
                        info =>
                            info.Exists &&
                            !IsReparsePoint(
                                info))
                    .OrderByDescending(
                        info =>
                            SafeLastWriteUtc(
                                info))
                    .Skip(
                        MaxQuarantineFiles)
                    .ToList();

            foreach (var file in
                     files)
            {
                TryDeleteFile(
                    file.FullName);
            }
        }
        catch
        {
            // Cleanup failure is harmless.
        }
    }

    private byte[] ReadAllBytesBounded(
        string path,
        int maximumBytes)
    {
        EnsureExistingFileIsRegular(
            path);

        var info =
            new FileInfo(
                path);

        if (
            info.Length <=
                0 ||
            info.Length >
                maximumBytes)
        {
            throw new InvalidDataException(
                "ADT calibration storage has an invalid file size.");
        }

        using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                32 * 1024,
                FileOptions.SequentialScan);

        if (
            stream.Length <=
                0 ||
            stream.Length >
                maximumBytes)
        {
            throw new InvalidDataException(
                "ADT calibration storage has an invalid file size.");
        }

        var bytes =
            new byte[
                (int)stream.Length];

        var offset =
            0;

        while (offset <
               bytes.Length)
        {
            var read =
                stream.Read(
                    bytes,
                    offset,
                    bytes.Length -
                    offset);

            if (read ==
                0)
            {
                throw new EndOfStreamException(
                    "ADT calibration storage ended unexpectedly while being read.");
            }

            offset +=
                read;
        }

        return
            bytes;
    }

    private void EnsureSafeDirectoryForWrite()
    {
        Directory.CreateDirectory(
            _directory);

        var info =
            new DirectoryInfo(
                _directory);

        if (!info.Exists)
        {
            throw new DirectoryNotFoundException(
                "ADT calibration data directory could not be created.");
        }

        if (IsReparsePoint(
                info))
        {
            throw new IOException(
                "ADT refused to write calibration data through a reparse-point directory.");
        }

        if (
            File.Exists(
                _path) &&
            IsReparsePoint(
                new FileInfo(
                    _path)))
        {
            throw new IOException(
                "ADT refused to replace a calibration file that is a reparse point.");
        }

        if (
            File.Exists(
                _backupPath) &&
            IsReparsePoint(
                new FileInfo(
                    _backupPath)))
        {
            throw new IOException(
                "ADT refused to replace a calibration backup that is a reparse point.");
        }
    }

    private static void EnsureExistingFileIsRegular(
        string path)
    {
        if (!File.Exists(
                path))
        {
            throw new FileNotFoundException(
                "ADT calibration storage file was not found.",
                path);
        }

        var info =
            new FileInfo(
                path);

        if (
            (info.Attributes &
             FileAttributes.ReparsePoint) !=
            0)
        {
            throw new IOException(
                "ADT refused to read calibration storage through a reparse point.");
        }
    }

    private InterprocessLease AcquireInterprocessLock()
    {
        var name =
            OperatingSystem.IsWindows()
                ? @"Local\AtomicDriftTuner.Calibrations.v1"
                : "AtomicDriftTuner.Calibrations.v1";

        var mutex =
            new Mutex(
                initiallyOwned:
                    false,
                name:
                    name);

        var acquired =
            false;

        try
        {
            acquired =
                mutex.WaitOne(
                    InterprocessLockTimeout);
        }
        catch (AbandonedMutexException)
        {
            acquired =
                true;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        if (!acquired)
        {
            mutex.Dispose();

            throw new IOException(
                "ADT could not get exclusive access to calibration storage because another ADT process is using it.");
        }

        return new InterprocessLease(
            mutex);
    }

    private static CalibrationProfile Clone(
        CalibrationProfile calibration)
    {
        return new CalibrationProfile
        {
            Key =
                calibration.Key,

            Samples =
                calibration.Samples,

            UpdatedUtc =
                calibration.UpdatedUtc,

            TorqueLimitDelta =
                calibration.TorqueLimitDelta,

            WheelSpeedDelta =
                calibration.WheelSpeedDelta,

            DampingDelta =
                calibration.DampingDelta,

            FrictionDelta =
                calibration.FrictionDelta,

            SpeedDampingDelta =
                calibration.SpeedDampingDelta,

            InterpolationDelta =
                calibration.InterpolationDelta,

            AcGainDelta =
                calibration.AcGainDelta
        };
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        foreach (var property in
                 element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                value =
                    property.Value;

                return true;
            }
        }

        value =
            default;

        return false;
    }

    private static bool IsRecoverableStorageException(
        Exception exception)
    {
        return exception is
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            NotSupportedException or
            JsonException;
    }

    private static bool IsReparsePoint(
        FileSystemInfo info)
    {
        try
        {
            return (
                info.Attributes &
                FileAttributes.ReparsePoint
            ) !=
            0;
        }
        catch
        {
            return true;
        }
    }

    private static DateTime SafeLastWriteUtc(
        FileInfo info)
    {
        try
        {
            return
                info.LastWriteTimeUtc;
        }
        catch
        {
            return
                DateTime.MinValue;
        }
    }

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
            // Cleanup failure must not hide the primary operation result.
        }
    }

    private sealed class ExistingPrimarySnapshot
    {
        public ExistingPrimarySnapshot(
            byte[] bytes)
        {
            Bytes =
                bytes ??
                throw new ArgumentNullException(
                    nameof(bytes));
        }

        public byte[] Bytes { get; }
    }

    private sealed class InterprocessLease : IDisposable
    {
        private Mutex? _mutex;

        public InterprocessLease(
            Mutex mutex)
        {
            _mutex =
                mutex;
        }

        public void Dispose()
        {
            var mutex =
                Interlocked.Exchange(
                    ref _mutex,
                    null);

            if (mutex is null)
            {
                return;
            }

            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Defensive only; this lease should own the mutex here.
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }
}
