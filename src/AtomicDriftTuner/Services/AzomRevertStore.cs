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
/// Persists the pre-batch AZOM snapshot used by "Revert last apply".
///
/// A revert record is recovery data, not a generic tune file. It is accepted
/// only when it is recent, came from a readable AZOM snapshot, names only
/// ADT-approved live-write properties, and contains a valid target value for
/// every property it claims can be restored.
/// </summary>
public sealed class AzomRevertStore
{
    private const string DataDirectoryName =
        "AtomicDriftTuner";

    private const string RevertFileName =
        "azom-last-apply-backup.json";

    private const string RevertMutexName =
        @"Local\AtomicDriftTuner.AzomRevert.v1";

    private const int MaxRevertFileBytes =
        512 * 1024;

    private const int MaxChangedProperties =
        128;

    private const int MaxPropertyNameLength =
        256;

    // A "last apply" rollback is intended as near-term recovery. The record
    // does not carry a hardware serial/identity, so allowing an arbitrarily old
    // record would risk offering values captured from a different wheelbase
    // session as though they were current-session recovery data.
    private static readonly TimeSpan MaximumRevertRecordAge =
        TimeSpan.FromHours(
            24);

    private static readonly TimeSpan MaximumFutureClockSkew =
        TimeSpan.FromMinutes(
            2);

    private static readonly TimeSpan MaximumSnapshotAgeAtSave =
        TimeSpan.FromSeconds(
            30);

    private static readonly TimeSpan InterprocessLockTimeout =
        TimeSpan.FromSeconds(
            5);

    private static readonly TimeSpan StaleTemporaryFileAge =
        TimeSpan.FromHours(
            24);

    // All AzomRevertStore instances in this ADT process point to the same
    // LocalAppData file.
    private static readonly object FileGate =
        new();

    // Protect the same recovery file if a second ADT process is running under
    // the same Windows logon session.
    private static readonly Lazy<Mutex> InterprocessGate =
        new(
            () =>
                new Mutex(
                    initiallyOwned:
                        false,
                    name:
                        RevertMutexName),
            LazyThreadSafetyMode.ExecutionAndPublication);

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
                JsonCommentHandling.Skip
        };

    private static readonly JsonDocumentOptions DocumentOptions =
        new()
        {
            AllowTrailingCommas =
                true,

            CommentHandling =
                JsonCommentHandling.Skip
        };

    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier:
                false,
            throwOnInvalidBytes:
                true);

    private readonly string _directory;
    private readonly string _path;

    public AzomRevertStore()
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
                    RevertFileName));

        // Deliberately do not create the directory in the constructor. Merely
        // constructing a recovery store should not perform a filesystem write.
    }

    public void Save(
        AzomLiveSnapshot snapshot,
        IEnumerable<string> changedProperties)
    {
        ArgumentNullException.ThrowIfNull(
            snapshot);

        ArgumentNullException.ThrowIfNull(
            changedProperties);

        var properties =
            NormalizeChangedProperties(
                changedProperties);

        if (properties.Count ==
            0)
        {
            throw new ArgumentException(
                "ADT cannot create an AZOM revert record without at least one changed AZOM property.",
                nameof(changedProperties));
        }

        var now =
            DateTime.UtcNow;

        // Detach the recovery snapshot from the mutable live object supplied by
        // the caller before validation/persistence.
        var detachedSnapshot =
            CloneSnapshot(
                snapshot);

        ValidateSnapshotForRecord(
            detachedSnapshot,
            now,
            requireFreshForSave:
                true);

        ValidatePropertiesAgainstSnapshot(
            detachedSnapshot,
            properties);

        var record =
            new AzomRevertRecord
            {
                Schema =
                    AzomRevertRecord.CurrentSchema,

                SavedUtc =
                    now,

                Snapshot =
                    detachedSnapshot,

                ChangedProperties =
                    properties
            };

        var bytes =
            SerializeRecord(
                record);

        lock (FileGate)
        {
            using var lease =
                AcquireInterprocessGate();

            EnsureDirectory();

            CleanupStaleTemporaryFilesBestEffort();

            var previousBytes =
                TryReadCurrentValidBytes();

            WriteAtomicallyAndVerify(
                bytes,
                previousBytes);

            CleanupStaleTemporaryFilesBestEffort();
        }
    }

    public AzomRevertRecord? Load()
    {
        lock (FileGate)
        {
            try
            {
                using var lease =
                    AcquireInterprocessGate();

                CleanupStaleTemporaryFilesBestEffort();

                if (!IsSafeRegularFile(
                        _path))
                {
                    return null;
                }

                var bytes =
                    ReadAllBytesBounded(
                        _path);

                var record =
                    DeserializeRecord(
                        bytes);

                if (!ValidateLoadedRecord(
                        record))
                {
                    return null;
                }

                return
                    record;
            }
            catch
            {
                // A damaged, stale, inaccessible, locked, or otherwise
                // untrusted rollback record must never crash ADT or cause an
                // unvalidated wheelbase write. Leave it untouched for possible
                // diagnostics/manual recovery.
                return null;
            }
        }
    }

    private void WriteAtomicallyAndVerify(
        byte[] bytes,
        byte[]? previousBytes)
    {
        ValidateRecordBytes(
            bytes);

        ValidateSafeDestination();

        var temporaryPath =
            Path.Combine(
                _directory,
                $"{RevertFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (
                var stream =
                    new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        16 * 1024,
                        FileOptions.WriteThrough))
            {
                stream.Write(
                    bytes,
                    0,
                    bytes.Length);

                stream.Flush(
                    flushToDisk:
                        true);
            }

            ValidateRecordFile(
                temporaryPath);

            // Recheck immediately before publishing in case a reparse point or
            // directory was introduced at the destination after the first check.
            ValidateSafeDestination();

            File.Move(
                temporaryPath,
                _path,
                overwrite:
                    true);

            try
            {
                var written =
                    ReadAllBytesBounded(
                        _path);

                ValidateRecordBytes(
                    written);

                var expectedHash =
                    SHA256.HashData(
                        bytes);

                var actualHash =
                    SHA256.HashData(
                        written);

                if (!CryptographicOperations.FixedTimeEquals(
                        expectedHash,
                        actualHash))
                {
                    throw new IOException(
                        "ADT could not verify the AZOM revert record after saving it.");
                }
            }
            catch
            {
                RestorePreviousRecordBestEffort(
                    previousBytes);

                throw;
            }
        }
        finally
        {
            TryDeleteFile(
                temporaryPath);
        }
    }

    private byte[]? TryReadCurrentValidBytes()
    {
        try
        {
            if (!IsSafeRegularFile(
                    _path))
            {
                return null;
            }

            var bytes =
                ReadAllBytesBounded(
                    _path);

            var record =
                DeserializeRecord(
                    bytes);

            return ValidateLoadedRecord(
                    record)
                    ? bytes
                    : null;
        }
        catch
        {
            return null;
        }
    }

    private void RestorePreviousRecordBestEffort(
        byte[]? previousBytes)
    {
        try
        {
            if (previousBytes is
                { Length: > 0 })
            {
                var restorePath =
                    Path.Combine(
                        _directory,
                        $"{RevertFileName}.{Guid.NewGuid():N}.restore.tmp");

                try
                {
                    using (
                        var stream =
                            new FileStream(
                                restorePath,
                                FileMode.CreateNew,
                                FileAccess.Write,
                                FileShare.None,
                                16 * 1024,
                                FileOptions.WriteThrough))
                    {
                        stream.Write(
                            previousBytes,
                            0,
                            previousBytes.Length);

                        stream.Flush(
                            flushToDisk:
                                true);
                    }

                    ValidateRecordFile(
                        restorePath);

                    ValidateSafeDestination();

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

                return;
            }

            // There was no previously verified record. Do not leave a newly
            // published record in place after verification has failed.
            TryDeleteFile(
                _path);
        }
        catch
        {
            // Recovery is best-effort. The original save failure remains the
            // authoritative result.
        }
    }

    private static byte[] SerializeRecord(
        AzomRevertRecord record)
    {
        string json;

        try
        {
            json =
                JsonSerializer.Serialize(
                    record,
                    Json);
        }
        catch (Exception ex)
            when (
                ex is JsonException or
                NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not serialize the AZOM revert record.",
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
                "ADT AZOM revert data contained invalid text.",
                ex);
        }

        if (
            bytes.Length <=
                0 ||
            bytes.Length >
                MaxRevertFileBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to save an AZOM revert record outside the supported 1..{MaxRevertFileBytes:N0} byte range.");
        }

        return
            bytes;
    }

    private static AzomRevertRecord? DeserializeRecord(
        byte[] bytes)
    {
        ValidateRequiredEnvelopeFields(
            bytes);

        try
        {
            return
                JsonSerializer.Deserialize<AzomRevertRecord>(
                    bytes,
                    Json);
        }
        catch (Exception ex)
            when (
                ex is JsonException or
                NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not read the AZOM revert record.",
                ex);
        }
    }

    private static void ValidateRequiredEnvelopeFields(
        byte[] bytes)
    {
        try
        {
            using var document =
                JsonDocument.Parse(
                    bytes,
                    DocumentOptions);

            var root =
                document.RootElement;

            if (root.ValueKind !=
                JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "ADT AZOM revert record root must be a JSON object.");
            }

            if (
                !TryGetJsonPropertyIgnoreCase(
                    root,
                    "Schema",
                    out var schema) ||
                schema.ValueKind !=
                    JsonValueKind.String ||
                !string.Equals(
                    schema.GetString(),
                    AzomRevertRecord.CurrentSchema,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "ADT AZOM revert record is missing the supported schema identifier.");
            }

            if (
                !TryGetJsonPropertyIgnoreCase(
                    root,
                    "SavedUtc",
                    out var savedUtc) ||
                savedUtc.ValueKind !=
                    JsonValueKind.String ||
                !savedUtc.TryGetDateTime(
                    out _))
            {
                throw new InvalidDataException(
                    "ADT AZOM revert record is missing a valid SavedUtc timestamp.");
            }

            if (
                !TryGetJsonPropertyIgnoreCase(
                    root,
                    "ChangedProperties",
                    out var changedProperties) ||
                changedProperties.ValueKind !=
                    JsonValueKind.Array ||
                changedProperties.GetArrayLength() ==
                    0)
            {
                throw new InvalidDataException(
                    "ADT AZOM revert record is missing its changed-property list.");
            }

            if (
                !TryGetJsonPropertyIgnoreCase(
                    root,
                    "Snapshot",
                    out var snapshot) ||
                snapshot.ValueKind !=
                    JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "ADT AZOM revert record is missing its live snapshot.");
            }

            if (
                !TryGetJsonPropertyIgnoreCase(
                    snapshot,
                    "BridgeVersion",
                    out var bridgeVersion) ||
                bridgeVersion.ValueKind !=
                    JsonValueKind.String ||
                string.IsNullOrWhiteSpace(
                    bridgeVersion.GetString()))
            {
                throw new InvalidDataException(
                    "ADT AZOM revert snapshot is missing bridge identity.");
            }

            if (
                !TryGetJsonPropertyIgnoreCase(
                    snapshot,
                    "CapturedUtc",
                    out var capturedUtc) ||
                capturedUtc.ValueKind !=
                    JsonValueKind.String ||
                !capturedUtc.TryGetDateTime(
                    out _))
            {
                throw new InvalidDataException(
                    "ADT AZOM revert snapshot is missing a valid capture time.");
            }

            if (
                !TryGetJsonPropertyIgnoreCase(
                    snapshot,
                    "SettingsReadable",
                    out var settingsReadable) ||
                settingsReadable.ValueKind is not
                    JsonValueKind.True and not
                    JsonValueKind.False)
            {
                throw new InvalidDataException(
                    "ADT AZOM revert snapshot is missing its settings-readable state.");
            }

            if (
                !TryGetJsonPropertyIgnoreCase(
                    snapshot,
                    "PropertyNamespace",
                    out var propertyNamespace) ||
                propertyNamespace.ValueKind !=
                    JsonValueKind.String)
            {
                throw new InvalidDataException(
                    "ADT AZOM revert snapshot is missing its property namespace.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "ADT AZOM revert record is not valid JSON.",
                ex);
        }
    }

    private static bool TryGetJsonPropertyIgnoreCase(
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

    private static AzomLiveSnapshot CloneSnapshot(
        AzomLiveSnapshot snapshot)
    {
        try
        {
            var bytes =
                JsonSerializer.SerializeToUtf8Bytes(
                    snapshot,
                    Json);

            return
                JsonSerializer.Deserialize<AzomLiveSnapshot>(
                    bytes,
                    Json)
                ?? throw new InvalidDataException(
                    "ADT could not clone the live AZOM snapshot for recovery.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
            when (
                ex is JsonException or
                NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not create a stable AZOM recovery snapshot.",
                ex);
        }
    }

    private static void ValidateRecordBytes(
        byte[] bytes)
    {
        if (
            bytes.Length <=
                0 ||
            bytes.Length >
                MaxRevertFileBytes)
        {
            throw new InvalidDataException(
                $"ADT AZOM revert record size is outside the supported 1..{MaxRevertFileBytes:N0} byte range.");
        }

        var record =
            DeserializeRecord(
                bytes);

        if (!ValidateLoadedRecord(
                record))
        {
            throw new InvalidDataException(
                "ADT refused an AZOM revert record that failed recovery validation.");
        }
    }

    private static void ValidateRecordFile(
        string path)
    {
        var bytes =
            ReadAllBytesBounded(
                path);

        ValidateRecordBytes(
            bytes);
    }

    private static byte[] ReadAllBytesBounded(
        string path)
    {
        using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.SequentialScan);

        if (
            stream.Length <=
                0 ||
            stream.Length >
                MaxRevertFileBytes)
        {
            throw new InvalidDataException(
                $"ADT AZOM revert record size is outside the supported 1..{MaxRevertFileBytes:N0} byte range.");
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
                    "ADT reached the end of the AZOM revert record before all expected bytes were read.");
            }

            offset +=
                read;
        }

        return
            bytes;
    }

    private static List<string> NormalizeChangedProperties(
        IEnumerable<string> changedProperties)
    {
        var result =
            new List<string>();

        var seen =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var property in
                 changedProperties)
        {
            if (string.IsNullOrWhiteSpace(
                    property))
            {
                continue;
            }

            var normalized =
                property.Trim();

            if (
                normalized.Length >
                    MaxPropertyNameLength ||
                !string.Equals(
                    normalized,
                    property,
                    StringComparison.Ordinal) ||
                !IsSafeAzomName(
                    normalized))
            {
                throw new ArgumentException(
                    $"Invalid AZOM revert property name: '{normalized}'.",
                    nameof(changedProperties));
            }

            // Validate the property against the same central allow-list/type
            // contract used by the live-write controller. We supply neither
            // target here deliberately: approved properties fail only with a
            // target-type error; unknown properties fail as not allowed.
            if (!IsApprovedRevertProperty(
                    normalized))
            {
                throw new ArgumentException(
                    $"ADT does not allow '{normalized}' in an AZOM revert record.",
                    nameof(changedProperties));
            }

            if (seen.Add(
                    normalized))
            {
                result.Add(
                    normalized);
            }

            if (result.Count >
                MaxChangedProperties)
            {
                throw new ArgumentException(
                    $"An AZOM revert record cannot contain more than {MaxChangedProperties} changed properties.",
                    nameof(changedProperties));
            }
        }

        return
            result;
    }

    private static bool ValidateLoadedRecord(
        AzomRevertRecord? record)
    {
        if (
            record is null ||
            record.Snapshot is null ||
            record.ChangedProperties is null ||
            record.ChangedProperties.Count ==
                0 ||
            record.ChangedProperties.Count >
                MaxChangedProperties)
        {
            return false;
        }

        if (!string.Equals(
                record.Schema,
                AzomRevertRecord.CurrentSchema,
                StringComparison.Ordinal))
        {
            return false;
        }

        var now =
            DateTime.UtcNow;

        if (!TryNormalizeUtc(
                record.SavedUtc,
                out var savedUtc))
        {
            return false;
        }

        if (
            savedUtc >
                now +
                MaximumFutureClockSkew ||
            savedUtc <
                now -
                MaximumRevertRecordAge)
        {
            return false;
        }

        try
        {
            ValidateSnapshotForRecord(
                record.Snapshot,
                savedUtc,
                requireFreshForSave:
                    false);

            var normalized =
                NormalizeChangedProperties(
                    record.ChangedProperties);

            if (normalized.Count !=
                record.ChangedProperties.Count)
            {
                return false;
            }

            for (
                var index =
                    0;
                index <
                normalized.Count;
                index++)
            {
                if (!string.Equals(
                        normalized[index],
                        record.ChangedProperties[index],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            ValidatePropertiesAgainstSnapshot(
                record.Snapshot,
                normalized);

            record.SavedUtc =
                savedUtc;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateSnapshotForRecord(
        AzomLiveSnapshot snapshot,
        DateTime referenceUtc,
        bool requireFreshForSave)
    {
        if (!string.Equals(
                snapshot.PropertyNamespace,
                "AZOM",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "ADT refused an AZOM revert snapshot from an unsupported property namespace.");
        }

        if (!snapshot.SettingsReadable)
        {
            throw new InvalidDataException(
                "ADT refused an AZOM revert snapshot whose live settings were not safely readable.");
        }

        if (snapshot.BaseConnected ==
            false)
        {
            throw new InvalidDataException(
                "ADT refused an AZOM revert snapshot captured while the wheelbase was reported disconnected.");
        }

        if (
            string.IsNullOrWhiteSpace(
                snapshot.BridgeVersion) ||
            string.Equals(
                snapshot.BridgeVersion,
                "unknown",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "ADT refused an AZOM revert snapshot without valid bridge identity.");
        }

        if (!TryNormalizeUtc(
                snapshot.CapturedUtc,
                out var capturedUtc))
        {
            throw new InvalidDataException(
                "ADT refused an AZOM revert snapshot without a valid capture time.");
        }

        if (
            capturedUtc >
                referenceUtc +
                MaximumFutureClockSkew)
        {
            throw new InvalidDataException(
                "ADT refused an AZOM revert snapshot with a future capture time.");
        }

        if (
            capturedUtc <
                referenceUtc -
                MaximumSnapshotAgeAtSave)
        {
            throw new InvalidDataException(
                requireFreshForSave
                    ? "ADT refused to save an AZOM revert snapshot that was already stale."
                    : "ADT refused an AZOM revert record whose snapshot was stale when the record was created.");
        }

        snapshot.CapturedUtc =
            capturedUtc;
    }

    private static void ValidatePropertiesAgainstSnapshot(
        AzomLiveSnapshot snapshot,
        IReadOnlyCollection<string> properties)
    {
        foreach (var property in
                 properties)
        {
            if (!TryGetSnapshotTarget(
                    snapshot,
                    property,
                    out var targetInt,
                    out var targetBool))
            {
                throw new InvalidDataException(
                    $"ADT revert snapshot does not contain a usable value for '{property}'.");
            }

            AzomLiveController.ValidateDirectWriteTarget(
                property,
                targetInt,
                targetBool);
        }
    }

    private static bool TryGetSnapshotTarget(
        AzomLiveSnapshot snapshot,
        string property,
        out int? targetInt,
        out bool? targetBool)
    {
        targetInt =
            null;

        targetBool =
            null;

        switch (property)
        {
            case "AZOM.FfbStrength":
                targetInt = snapshot.FfbStrength;
                break;

            case "AZOM.Torque":
                targetInt = snapshot.Torque;
                break;

            case "AZOM.Rotation":
                targetInt = snapshot.Rotation;
                break;

            case "AZOM.WheelSpeedLimit":
                targetInt = snapshot.WheelSpeedLimit;
                break;

            case "AZOM.Interpolation":
                targetInt = snapshot.Interpolation;
                break;

            case "AZOM.GearshiftVibration":
                targetInt = snapshot.GearshiftVibration;
                break;

            case "AZOM.Damper":
                targetInt = snapshot.Damper;
                break;

            case "AZOM.Friction":
                targetInt = snapshot.Friction;
                break;

            case "AZOM.Inertia":
                targetInt = snapshot.Inertia;
                break;

            case "AZOM.Spring":
                targetInt = snapshot.Spring;
                break;

            case "AZOM.GameDamper":
                targetInt = snapshot.GameDamper;
                break;

            case "AZOM.GameFriction":
                targetInt = snapshot.GameFriction;
                break;

            case "AZOM.GameInertia":
                targetInt = snapshot.GameInertia;
                break;

            case "AZOM.GameSpring":
                targetInt = snapshot.GameSpring;
                break;

            case "AZOM.NaturalInertia":
                targetInt = snapshot.NaturalInertia;
                break;

            case "AZOM.SoftLimitStiffness":
                targetInt = snapshot.SoftLimitStiffness;
                break;

            case "AZOM.SpeedDamping":
                targetInt = snapshot.SpeedDamping;
                break;

            case "AZOM.SpeedDampingPoint":
                targetInt = snapshot.SpeedDampingPoint;
                break;

            case "AZOM.RoadSensitivity":
                targetInt = snapshot.RoadSensitivity;
                break;

            case "AZOM.Equalizer1":
                targetInt = snapshot.Equalizer1;
                break;

            case "AZOM.Equalizer2":
                targetInt = snapshot.Equalizer2;
                break;

            case "AZOM.Equalizer3":
                targetInt = snapshot.Equalizer3;
                break;

            case "AZOM.Equalizer4":
                targetInt = snapshot.Equalizer4;
                break;

            case "AZOM.Equalizer5":
                targetInt = snapshot.Equalizer5;
                break;

            case "AZOM.Equalizer6":
                targetInt = snapshot.Equalizer6;
                break;

            case "AZOM.Equalizer7":
                targetInt = snapshot.Equalizer7;
                break;

            case "AZOM.Equalizer8":
                targetInt = snapshot.Equalizer8;
                break;

            case "AZOM.Equalizer9":
                targetInt = snapshot.Equalizer9;
                break;

            case "AZOM.Equalizer10":
                targetInt = snapshot.Equalizer10;
                break;

            case "AZOM.FfbCurveX1":
                targetInt = snapshot.FfbCurveX1;
                break;

            case "AZOM.FfbCurveX2":
                targetInt = snapshot.FfbCurveX2;
                break;

            case "AZOM.FfbCurveX3":
                targetInt = snapshot.FfbCurveX3;
                break;

            case "AZOM.FfbCurveX4":
                targetInt = snapshot.FfbCurveX4;
                break;

            case "AZOM.FfbCurveY1":
                targetInt = snapshot.FfbCurveY1;
                break;

            case "AZOM.FfbCurveY2":
                targetInt = snapshot.FfbCurveY2;
                break;

            case "AZOM.FfbCurveY3":
                targetInt = snapshot.FfbCurveY3;
                break;

            case "AZOM.FfbCurveY4":
                targetInt = snapshot.FfbCurveY4;
                break;

            case "AZOM.FfbCurveY5":
                targetInt = snapshot.FfbCurveY5;
                break;

            case "AZOM.Protection":
                targetBool = snapshot.Protection;
                break;

            case "AZOM.SoftLimitRetain":
                targetBool = snapshot.SoftLimitRetain;
                break;

            case "AZOM.FfbReverse":
                targetBool = snapshot.FfbReverse;
                break;

            case "AZOM.BaseStatusLed":
                targetBool = snapshot.BaseStatusLed;
                break;

            case "AZOM.Bluetooth":
                targetBool = snapshot.Bluetooth;
                break;

            case "AZOM.WorkMode":
                if (snapshot.WorkMode.HasValue &&
                    snapshot.WorkMode.Value is
                        0 or
                        1)
                {
                    targetBool =
                        snapshot.WorkMode.Value ==
                        1;
                }

                break;

            default:
                return false;
        }

        return
            targetInt.HasValue ^
            targetBool.HasValue;
    }

    private static bool IsApprovedRevertProperty(
        string property)
    {
        try
        {
            // Probe the central live-write contract with the value shape that
            // belongs to this property. A range error still means the property
            // is known; an "not allowed" error means it is not.
            if (IsToggleProperty(
                    property))
            {
                AzomLiveController.ValidateDirectWriteTarget(
                    property,
                    targetInt:
                        null,
                    targetBool:
                        false);

                return true;
            }

            // Numeric properties need a known in-range probe. These neutral
            // values are used only to ask the controller whether the property
            // is part of the write contract; actual snapshot values are
            // validated separately before persistence/use.
            var probe =
                property switch
                {
                    "AZOM.Torque" =>
                        50,

                    "AZOM.Rotation" =>
                        60,

                    "AZOM.Inertia" =>
                        100,

                    "AZOM.NaturalInertia" =>
                        100,

                    "AZOM.SoftLimitStiffness" =>
                        1,

                    _ =>
                        0
                };

            AzomLiveController.ValidateDirectWriteTarget(
                property,
                targetInt:
                    probe,
                targetBool:
                    null);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsToggleProperty(
        string property)
    {
        return property is
            "AZOM.Protection" or
            "AZOM.SoftLimitRetain" or
            "AZOM.FfbReverse" or
            "AZOM.BaseStatusLed" or
            "AZOM.Bluetooth" or
            "AZOM.WorkMode";
    }

    private static bool IsSafeAzomName(
        string value)
    {
        if (
            string.IsNullOrWhiteSpace(
                value) ||
            value.Length >
                MaxPropertyNameLength ||
            !value.StartsWith(
                "AZOM.",
                StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in
                 value)
        {
            if (
                !char.IsLetterOrDigit(
                    character) &&
                character !=
                    '.' &&
                character !=
                    '_' &&
                character !=
                    '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryNormalizeUtc(
        DateTime value,
        out DateTime utc)
    {
        utc =
            default;

        if (value ==
            default)
        {
            return false;
        }

        try
        {
            utc =
                value.Kind switch
                {
                    DateTimeKind.Utc =>
                        value,

                    DateTimeKind.Local =>
                        value.ToUniversalTime(),

                    DateTimeKind.Unspecified =>
                        throw new InvalidDataException(
                            "ADT recovery timestamps must include UTC/local time semantics."),

                    _ =>
                        throw new InvalidDataException(
                            "ADT recovery timestamp kind is invalid.")
                };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ValidateSafeDestination()
    {
        if (Directory.Exists(
                _path))
        {
            throw new InvalidOperationException(
                "ADT refused to replace the AZOM revert record because that path is a directory.");
        }

        if (!File.Exists(
                _path))
        {
            return;
        }

        var attributes =
            File.GetAttributes(
                _path);

        if (
            (attributes &
             FileAttributes.Directory) !=
                0 ||
            (attributes &
             FileAttributes.ReparsePoint) !=
                0)
        {
            throw new InvalidOperationException(
                "ADT refused to replace the AZOM revert record because it is not a normal local file.");
        }
    }

    private static bool IsSafeRegularFile(
        string path)
    {
        try
        {
            if (!File.Exists(
                    path))
            {
                return false;
            }

            var attributes =
                File.GetAttributes(
                    path);

            return
                (attributes &
                 FileAttributes.Directory) ==
                    0 &&
                (attributes &
                 FileAttributes.ReparsePoint) ==
                    0;
        }
        catch
        {
            return false;
        }
    }

    private void CleanupStaleTemporaryFilesBestEffort()
    {
        if (!Directory.Exists(
                _directory))
        {
            return;
        }

        try
        {
            var cutoff =
                DateTime.UtcNow -
                StaleTemporaryFileAge;

            foreach (var path in
                     Directory.EnumerateFiles(
                         _directory,
                         $"{RevertFileName}.*.tmp",
                         SearchOption.TopDirectoryOnly)
                         .Take(
                             32))
            {
                try
                {
                    var info =
                        new FileInfo(
                            path);

                    if (
                        !info.Exists ||
                        (info.Attributes &
                         FileAttributes.ReparsePoint) !=
                            0 ||
                        info.LastWriteTimeUtc >
                            cutoff)
                    {
                        continue;
                    }

                    info.Delete();
                }
                catch
                {
                    // Stale temporary-file cleanup is best-effort.
                }
            }
        }
        catch
        {
            // Directory enumeration itself is best-effort.
        }
    }

    private static InterprocessMutexLease AcquireInterprocessGate()
    {
        var mutex =
            InterprocessGate.Value;

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

        if (!acquired)
        {
            throw new IOException(
                "ADT could not get exclusive access to the AZOM revert record because another ADT process is using it.");
        }

        return
            new InterprocessMutexLease(
                mutex);
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(
            _directory);
    }

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(
                    path))
            {
                var attributes =
                    File.GetAttributes(
                        path);

                if ((attributes &
                     FileAttributes.ReparsePoint) !=
                    0)
                {
                    return;
                }

                File.Delete(
                    path);
            }
        }
        catch
        {
            // Cleanup must never hide the operation's real result.
        }
    }

    private sealed class InterprocessMutexLease : IDisposable
    {
        private Mutex? _mutex;

        public InterprocessMutexLease(
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
                // Defensive only: this lease should always be owned here.
            }
        }
    }
}
