using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AppSettingsStore
{
    private const string DataDirectoryName =
        "AtomicDriftTuner";

    private const string SettingsFileName =
        "settings.json";

    private const string BackupFileName =
        "settings.backup.json";

    private const string SettingsMutexName =
        @"Local\AtomicDriftTuner.Settings.v1";

    private const int MaxSettingsFileBytes =
        1024 * 1024;

    private const long MaxQuarantineFileBytes =
        4L * 1024L * 1024L;

    private const int MaxInvalidSettingsFilesToKeep =
        3;

    private static readonly TimeSpan InterprocessLockTimeout =
        TimeSpan.FromSeconds(
            5);

    private static readonly TimeSpan StaleTemporaryFileAge =
        TimeSpan.FromHours(
            24);

    // FileGate protects all AppSettingsStore instances inside this ADT process.
    // The named mutex extends that protection to another ADT process running in
    // the same Windows logon session.
    private static readonly object FileGate =
        new();

    private static readonly Lazy<Mutex> InterprocessGate =
        new(
            () =>
                new Mutex(
                    initiallyOwned:
                        false,
                    name:
                        SettingsMutexName),
            LazyThreadSafetyMode.ExecutionAndPublication);

    // A loaded settings object carries the revision it was based on. Save()
    // checks that revision before replacing the file so two windows/processes
    // cannot silently overwrite each other's newer settings.
    private static readonly ConditionalWeakTable<AppSettings, SettingsSnapshot>
        Snapshots =
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
                JsonCommentHandling.Skip
        };

    private static readonly JsonDocumentOptions JsonDocument =
        new()
        {
            AllowTrailingCommas =
                true,

            CommentHandling =
                JsonCommentHandling.Skip
        };

    private readonly string _directory;
    private readonly string _path;
    private readonly string _backupPath;

    public AppSettingsStore()
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
                    SettingsFileName));

        _backupPath =
            Path.GetFullPath(
                Path.Combine(
                    _directory,
                    BackupFileName));

        // Deliberately do not create the directory here. Constructing a store
        // must remain side-effect free so read-only startup can still fall back
        // safely if LocalAppData is temporarily unwritable.
    }

    public AppSettings Load()
    {
        lock (FileGate)
        {
            try
            {
                using var lease =
                    AcquireInterprocessGate();

                CleanupStaleTemporaryFilesBestEffort();

                var primary =
                    ReadSettingsFileState(
                        _path);

                if (
                    primary.Valid &&
                    primary.Settings is not null)
                {
                    TrackSnapshot(
                        primary.Settings,
                        new SettingsSnapshot(
                            primary.Revision,
                            primary.Bytes,
                            SaveAllowed:
                                primary.CanCompareRevision));

                    return
                        primary.Settings;
                }

                var backup =
                    ReadSettingsFileState(
                        _backupPath);

                if (
                    backup.Valid &&
                    backup.Settings is not null)
                {
                    // The primary remains untouched for diagnostics/manual
                    // recovery. Save() may later repair it, but only if the
                    // primary revision still matches what this Load() observed.
                    TrackSnapshot(
                        backup.Settings,
                        new SettingsSnapshot(
                            primary.Revision,
                            backup.Bytes,
                            SaveAllowed:
                                primary.CanCompareRevision));

                    return
                        backup.Settings;
                }

                var defaults =
                    CreateDefaults();

                TrackSnapshot(
                    defaults,
                    new SettingsSnapshot(
                        primary.Revision,
                        SourceBytes:
                            null,
                        SaveAllowed:
                            primary.CanCompareRevision));

                return
                    defaults;
            }
            catch
            {
                // A corrupt, locked, inaccessible, or temporarily unavailable
                // settings store must not prevent ADT from opening.
                //
                // These defaults are marked non-saveable because ADT did not get
                // a trustworthy revision of the persisted settings. This avoids
                // converting a transient read failure into destructive data loss.
                var defaults =
                    CreateDefaults();

                TrackSnapshot(
                    defaults,
                    new SettingsSnapshot(
                        PrimaryRevision:
                            SettingsSnapshot.UnavailableRevision,
                        SourceBytes:
                            null,
                        SaveAllowed:
                            false));

                return
                    defaults;
            }
        }
    }

    public void Save(
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(
            settings);

        lock (FileGate)
        {
            using var lease =
                AcquireInterprocessGate();

            EnsureDirectory();

            CleanupStaleTemporaryFilesBestEffort();

            var current =
                ReadSettingsFileState(
                    _path);

            var tracked =
                Snapshots.TryGetValue(
                    settings,
                    out var snapshot);

            if (tracked)
            {
                if (!snapshot!.SaveAllowed)
                {
                    throw new InvalidOperationException(
                        "ADT did not get a trustworthy settings snapshot when these values were loaded. Reload the settings and try again instead of overwriting the existing configuration.");
                }

                if (!string.Equals(
                        snapshot.PrimaryRevision,
                        current.Revision,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "ADT settings changed after this copy was loaded. Reload the current settings and retry your change so newer settings are not overwritten.");
                }
            }
            else if (
                current.Exists ||
                File.Exists(
                    _backupPath))
            {
                // Internal ADT callers are expected to Load(), change the fields
                // they own, then Save() that same object. Refuse an untracked
                // whole-object overwrite when persisted settings already exist.
                throw new InvalidOperationException(
                    "ADT refused to overwrite existing application settings with an untracked settings object. Load the current settings first, apply the intended change, and save that loaded object.");
            }

            if (
                current.Exists &&
                !current.SafeRegularFile)
            {
                throw new InvalidOperationException(
                    "ADT refused to replace settings.json because it is not a normal local file.");
            }

            var backup =
                ReadSettingsFileState(
                    _backupPath);

            if (
                backup.Exists &&
                !backup.SafeRegularFile)
            {
                throw new InvalidOperationException(
                    "ADT refused to replace the settings backup because it is not a normal local file.");
            }

            ApplyTopLevelDefaults(
                settings);

            var bytes =
                SerializeSettings(
                    settings);

            if (
                tracked &&
                snapshot!.SourceBytes is
                    { Length: > 0 })
            {
                // Preserve JSON fields written by a newer ADT build that this
                // older model does not know about. This makes downgrade/recovery
                // saves less destructive while still serializing all properties
                // understood by the current build.
                bytes =
                    PreserveUnknownJsonFields(
                        bytes,
                        snapshot.SourceBytes);
            }

            ValidateSettingsBytes(
                bytes);

            string? quarantinedPrimary =
                null;

            // Before replacing a known-good primary, preserve it as the rollback
            // backup. A failed backup update aborts before settings.json changes.
            if (
                current.Valid &&
                current.Bytes is
                    { Length: > 0 })
            {
                WriteVerifiedSettingsFile(
                    _backupPath,
                    current.Bytes);
            }
            else if (current.Exists)
            {
                // Do not silently destroy a corrupt/oversized settings file.
                // Move it aside first so it remains available for recovery.
                quarantinedPrimary =
                    QuarantineInvalidPrimary(
                        current);
            }

            var restoreBytes =
                current.Valid &&
                current.Bytes is
                    { Length: > 0 }
                    ? current.Bytes
                    : backup.Valid &&
                      backup.Bytes is
                          { Length: > 0 }
                        ? backup.Bytes
                        : null;

            try
            {
                WriteVerifiedSettingsFile(
                    _path,
                    bytes);
            }
            catch
            {
                RestorePrimaryBestEffort(
                    restoreBytes,
                    quarantinedPrimary);

                throw;
            }

            var saved =
                ReadSettingsFileState(
                    _path);

            if (
                !saved.Valid ||
                saved.Bytes is null)
            {
                RestorePrimaryBestEffort(
                    restoreBytes,
                    quarantinedPrimary);

                throw new IOException(
                    "ADT wrote settings.json but could not verify the saved settings afterward.");
            }

            var expectedHash =
                ComputeSha256(
                    bytes);

            var actualHash =
                ComputeSha256(
                    saved.Bytes);

            if (!CryptographicOperations.FixedTimeEquals(
                    expectedHash,
                    actualHash))
            {
                RestorePrimaryBestEffort(
                    restoreBytes,
                    quarantinedPrimary);

                throw new IOException(
                    "ADT settings verification failed because the saved file does not match the intended settings.");
            }

            // If there was no valid rollback backup before this successful save,
            // create a same-value recovery copy. Backup creation here is
            // best-effort because the primary is already fully verified.
            if (!backup.Valid)
            {
                TryWriteRecoveryBackup(
                    bytes);
            }

            TrackSnapshot(
                settings,
                new SettingsSnapshot(
                    saved.Revision,
                    bytes,
                    SaveAllowed:
                        true));

            CleanupInvalidSettingsHistoryBestEffort();

            CleanupStaleTemporaryFilesBestEffort();
        }
    }

    private SettingsFileState ReadSettingsFileState(
        string path)
    {
        FileAttributes attributes;

        try
        {
            attributes =
                File.GetAttributes(
                    path);
        }
        catch (FileNotFoundException)
        {
            return
                SettingsFileState.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return
                SettingsFileState.Missing;
        }
        catch (Exception ex)
            when (IsRecoverableFileException(
                ex))
        {
            return
                SettingsFileState.Unavailable;
        }

        if (
            (attributes &
             FileAttributes.Directory) !=
                0 ||
            (attributes &
             FileAttributes.ReparsePoint) !=
                0)
        {
            return new SettingsFileState(
                Exists:
                    true,
                SafeRegularFile:
                    false,
                Valid:
                    false,
                CanCompareRevision:
                    true,
                Revision:
                    BuildMetadataRevision(
                        path,
                        attributes),
                Length:
                    SafeLength(
                        path),
                Bytes:
                    null,
                Settings:
                    null);
        }

        long length;

        try
        {
            length =
                new FileInfo(
                    path)
                    .Length;
        }
        catch (Exception ex)
            when (IsRecoverableFileException(
                ex))
        {
            return
                SettingsFileState.Unavailable;
        }

        if (
            length <=
                0 ||
            length >
                MaxSettingsFileBytes)
        {
            return new SettingsFileState(
                Exists:
                    true,
                SafeRegularFile:
                    true,
                Valid:
                    false,
                CanCompareRevision:
                    true,
                Revision:
                    BuildMetadataRevision(
                        path,
                        attributes),
                Length:
                    length,
                Bytes:
                    null,
                Settings:
                    null);
        }

        byte[] bytes;

        try
        {
            bytes =
                ReadAllBytesBounded(
                    path,
                    MaxSettingsFileBytes);
        }
        catch (Exception ex)
            when (IsRecoverableFileException(
                ex))
        {
            return new SettingsFileState(
                Exists:
                    true,
                SafeRegularFile:
                    true,
                Valid:
                    false,
                CanCompareRevision:
                    false,
                Revision:
                    SettingsSnapshot.UnavailableRevision,
                Length:
                    length,
                Bytes:
                    null,
                Settings:
                    null);
        }

        var revision =
            RevisionForBytes(
                bytes);

        try
        {
            var settings =
                JsonSerializer.Deserialize<AppSettings>(
                    bytes,
                    Json);

            if (settings is null)
            {
                return new SettingsFileState(
                    Exists:
                        true,
                    SafeRegularFile:
                        true,
                    Valid:
                        false,
                    CanCompareRevision:
                        true,
                    Revision:
                        revision,
                    Length:
                        bytes.LongLength,
                    Bytes:
                        bytes,
                    Settings:
                        null);
            }

            ApplyTopLevelDefaults(
                settings);

            return new SettingsFileState(
                Exists:
                    true,
                SafeRegularFile:
                    true,
                Valid:
                    true,
                CanCompareRevision:
                    true,
                Revision:
                    revision,
                Length:
                    bytes.LongLength,
                Bytes:
                    bytes,
                Settings:
                    settings);
        }
        catch (Exception ex)
            when (
                ex is JsonException or
                NotSupportedException)
        {
            return new SettingsFileState(
                Exists:
                    true,
                SafeRegularFile:
                    true,
                Valid:
                    false,
                CanCompareRevision:
                    true,
                Revision:
                    revision,
                Length:
                    bytes.LongLength,
                Bytes:
                    bytes,
                Settings:
                    null);
        }
    }

    private void WriteVerifiedSettingsFile(
        string targetPath,
        byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(
            bytes);

        if (
            bytes.Length <=
                0 ||
            bytes.Length >
                MaxSettingsFileBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to write a settings file outside the supported 1..{MaxSettingsFileBytes:N0} byte range.");
        }

        ValidateSafeDestination(
            targetPath);

        var temporaryPath =
            Path.Combine(
                _directory,
                $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

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

            ValidateSettingsBytesFromFile(
                temporaryPath);

            // Recheck the destination immediately before publication so a
            // reparse point cannot be introduced unnoticed between validation
            // and the final same-directory rename.
            ValidateSafeDestination(
                targetPath);

            File.Move(
                temporaryPath,
                targetPath,
                overwrite:
                    true);

            ValidateSettingsBytesFromFile(
                targetPath);

            var written =
                ReadAllBytesBounded(
                    targetPath,
                    MaxSettingsFileBytes);

            var expectedHash =
                ComputeSha256(
                    bytes);

            var writtenHash =
                ComputeSha256(
                    written);

            if (!CryptographicOperations.FixedTimeEquals(
                    expectedHash,
                    writtenHash))
            {
                throw new IOException(
                    $"ADT could not verify {Path.GetFileName(targetPath)} after writing it.");
            }
        }
        finally
        {
            TryDeleteFile(
                temporaryPath);
        }
    }

    private string? QuarantineInvalidPrimary(
        SettingsFileState current)
    {
        if (!current.Exists)
        {
            return null;
        }

        if (!current.SafeRegularFile)
        {
            throw new InvalidOperationException(
                "ADT refused to quarantine settings.json because it is not a normal local file.");
        }

        if (
            current.Length >
            MaxQuarantineFileBytes)
        {
            throw new InvalidDataException(
                $"The existing ADT settings file is invalid and is too large to quarantine automatically ({current.Length:N0} bytes). Move or rename settings.json manually before saving new settings.");
        }

        var quarantinedPath =
            Path.Combine(
                _directory,
                $"settings.invalid-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");

        try
        {
            File.Move(
                _path,
                quarantinedPath,
                overwrite:
                    false);

            return
                quarantinedPath;
        }
        catch (Exception ex)
            when (IsRecoverableFileException(
                ex))
        {
            throw new IOException(
                "ADT found an invalid settings.json but could not preserve it for recovery before saving new settings.",
                ex);
        }
    }

    private void RestorePrimaryBestEffort(
        byte[]? restoreBytes,
        string? quarantinedPrimary)
    {
        try
        {
            if (
                restoreBytes is
                    { Length: > 0 })
            {
                WriteVerifiedSettingsFile(
                    _path,
                    restoreBytes);

                return;
            }
        }
        catch
        {
            // Fall through to the quarantined original if one exists.
        }

        if (string.IsNullOrWhiteSpace(
                quarantinedPrimary))
        {
            return;
        }

        try
        {
            if (
                File.Exists(
                    quarantinedPrimary) &&
                !File.Exists(
                    _path))
            {
                File.Move(
                    quarantinedPrimary,
                    _path,
                    overwrite:
                        false);
            }
        }
        catch
        {
            // Recovery is best-effort. The preserved quarantine file remains in
            // LocalAppData if it could not be moved back into place.
        }
    }

    private void TryWriteRecoveryBackup(
        byte[] bytes)
    {
        try
        {
            WriteVerifiedSettingsFile(
                _backupPath,
                bytes);
        }
        catch
        {
            // settings.json is already verified. A missing backup must not turn
            // a successful user save into a false failure.
        }
    }

    private static byte[] SerializeSettings(
        AppSettings settings)
    {
        string json;

        try
        {
            json =
                JsonSerializer.Serialize(
                    settings,
                    Json);
        }
        catch (Exception ex)
            when (
                ex is JsonException or
                NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not serialize application settings.",
                ex);
        }

        byte[] bytes;

        try
        {
            bytes =
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false,
                    throwOnInvalidBytes:
                        true)
                .GetBytes(
                    json);
        }
        catch (EncoderFallbackException ex)
        {
            throw new InvalidDataException(
                "ADT application settings contained invalid text data.",
                ex);
        }

        if (
            bytes.Length <=
                0 ||
            bytes.Length >
                MaxSettingsFileBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to save a settings file outside the supported 1..{MaxSettingsFileBytes:N0} byte range.");
        }

        return
            bytes;
    }

    private static byte[] PreserveUnknownJsonFields(
        byte[] serializedCurrentSettings,
        byte[] sourceSettingsBytes)
    {
        try
        {
            var currentNode =
                JsonNode.Parse(
                    Encoding.UTF8.GetString(
                        serializedCurrentSettings),
                    documentOptions:
                        JsonDocument);

            var sourceNode =
                JsonNode.Parse(
                    Encoding.UTF8.GetString(
                        sourceSettingsBytes),
                    documentOptions:
                        JsonDocument);

            if (
                currentNode is not JsonObject currentObject ||
                sourceNode is not JsonObject sourceObject)
            {
                return
                    serializedCurrentSettings;
            }

            PreserveUnknownJsonNodes(
                currentObject,
                sourceObject);

            var mergedJson =
                currentObject.ToJsonString(
                    Json);

            var mergedBytes =
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false,
                    throwOnInvalidBytes:
                        true)
                .GetBytes(
                    mergedJson);

            if (mergedBytes.Length >
                MaxSettingsFileBytes)
            {
                throw new InvalidDataException(
                    "ADT preserved forward-compatible settings fields, but the merged settings file would exceed the supported size limit.");
            }

            return
                mergedBytes;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
            when (
                ex is JsonException or
                EncoderFallbackException or
                InvalidOperationException)
        {
            throw new InvalidDataException(
                "ADT could not preserve forward-compatible settings fields safely. The existing settings were not overwritten.",
                ex);
        }
    }

    private static void PreserveUnknownJsonNodes(
        JsonNode? target,
        JsonNode? source)
    {
        if (
            target is JsonObject targetObject &&
            source is JsonObject sourceObject)
        {
            foreach (
                var sourceProperty in
                sourceObject.ToList())
            {
                var targetKey =
                    targetObject
                        .Select(
                            item =>
                                item.Key)
                        .FirstOrDefault(
                            key =>
                                string.Equals(
                                    key,
                                    sourceProperty.Key,
                                    StringComparison.OrdinalIgnoreCase));

                if (targetKey is null)
                {
                    targetObject[sourceProperty.Key] =
                        sourceProperty.Value?.DeepClone();

                    continue;
                }

                PreserveUnknownJsonNodes(
                    targetObject[targetKey],
                    sourceProperty.Value);
            }

            return;
        }

        if (
            target is JsonArray targetArray &&
            source is JsonArray sourceArray &&
            targetArray.Count ==
            sourceArray.Count)
        {
            for (
                var index =
                    0;
                index <
                targetArray.Count;
                index++)
            {
                PreserveUnknownJsonNodes(
                    targetArray[index],
                    sourceArray[index]);
            }
        }
    }

    private static void ApplyTopLevelDefaults(
        AppSettings settings)
    {
        var defaults =
            CreateDefaults();

        foreach (
            var property in
            typeof(AppSettings).GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance))
        {
            if (
                !property.CanRead ||
                !property.CanWrite ||
                property.GetIndexParameters()
                    .Length !=
                0)
            {
                continue;
            }

            object? currentValue;
            object? defaultValue;

            try
            {
                currentValue =
                    property.GetValue(
                        settings);

                if (currentValue is not null)
                {
                    continue;
                }

                defaultValue =
                    property.GetValue(
                        defaults);
            }
            catch
            {
                continue;
            }

            if (defaultValue is null)
            {
                continue;
            }

            try
            {
                property.SetValue(
                    settings,
                    defaultValue);
            }
            catch
            {
                // A property that cannot be restored reflectively remains as
                // deserialized. Individual consumers still retain their own
                // null/default handling.
            }
        }
    }

    private static void ValidateSettingsBytes(
        byte[] bytes)
    {
        try
        {
            var settings =
                JsonSerializer.Deserialize<AppSettings>(
                    bytes,
                    Json);

            if (settings is null)
            {
                throw new InvalidDataException(
                    "ADT settings validation returned no settings object.");
            }

            ApplyTopLevelDefaults(
                settings);
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
                "ADT refused to save settings that could not be read back safely.",
                ex);
        }
    }

    private static void ValidateSettingsBytesFromFile(
        string path)
    {
        var bytes =
            ReadAllBytesBounded(
                path,
                MaxSettingsFileBytes);

        ValidateSettingsBytes(
            bytes);
    }

    private static byte[] ReadAllBytesBounded(
        string path,
        int maximumBytes)
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
                maximumBytes)
        {
            throw new InvalidDataException(
                $"ADT settings file size is outside the supported 1..{maximumBytes:N0} byte range.");
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
                    "ADT reached the end of the settings file before all expected bytes were read.");
            }

            offset +=
                read;
        }

        return
            bytes;
    }

    private void ValidateSafeDestination(
        string path)
    {
        if (Directory.Exists(
                path))
        {
            throw new InvalidOperationException(
                $"ADT refused to replace {Path.GetFileName(path)} because that path is a directory.");
        }

        if (!File.Exists(
                path))
        {
            return;
        }

        FileAttributes attributes;

        try
        {
            attributes =
                File.GetAttributes(
                    path);
        }
        catch (Exception ex)
            when (IsRecoverableFileException(
                ex))
        {
            throw new IOException(
                $"ADT could not inspect {Path.GetFileName(path)} before replacing it.",
                ex);
        }

        if (
            (attributes &
             FileAttributes.Directory) !=
                0 ||
            (attributes &
             FileAttributes.ReparsePoint) !=
                0)
        {
            throw new InvalidOperationException(
                $"ADT refused to replace {Path.GetFileName(path)} because it is not a normal local file.");
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
            // The previous ADT process terminated while holding the mutex.
            // Windows grants ownership to this process in that case.
            acquired =
                true;
        }

        if (!acquired)
        {
            throw new IOException(
                "ADT could not get exclusive access to application settings because another ADT process is using them. Try again in a moment.");
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

            foreach (
                var path in
                Directory.EnumerateFiles(
                    _directory,
                    "settings*.tmp",
                    SearchOption.TopDirectoryOnly)
                    .Take(
                        50))
            {
                try
                {
                    var info =
                        new FileInfo(
                            path);

                    if (
                        !info.Exists ||
                        (
                            info.Attributes &
                            FileAttributes.ReparsePoint
                        ) !=
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
                    // Stale-temp cleanup is best-effort only.
                }
            }
        }
        catch
        {
            // Directory enumeration itself is best-effort.
        }
    }

    private void CleanupInvalidSettingsHistoryBestEffort()
    {
        try
        {
            var invalidFiles =
                Directory
                    .EnumerateFiles(
                        _directory,
                        "settings.invalid-*.json",
                        SearchOption.TopDirectoryOnly)
                    .Select(
                        path =>
                            new FileInfo(
                                path))
                    .Where(
                        info =>
                            info.Exists &&
                            (
                                info.Attributes &
                                FileAttributes.ReparsePoint
                            ) ==
                            0)
                    .OrderByDescending(
                        info =>
                            info.LastWriteTimeUtc)
                    .Skip(
                        MaxInvalidSettingsFilesToKeep)
                    .ToList();

            foreach (
                var file in
                invalidFiles)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // Recovery-history cleanup is best-effort only.
                }
            }
        }
        catch
        {
            // Directory enumeration itself is best-effort.
        }
    }

    private static void TrackSnapshot(
        AppSettings settings,
        SettingsSnapshot snapshot)
    {
        Snapshots.Remove(
            settings);

        Snapshots.Add(
            settings,
            snapshot);
    }

    private static string RevisionForBytes(
        byte[] bytes)
    {
        return
            "SHA256:" +
            Convert.ToHexString(
                ComputeSha256(
                    bytes));
    }

    private static string BuildMetadataRevision(
        string path,
        FileAttributes attributes)
    {
        try
        {
            var info =
                new FileInfo(
                    path);

            return
                $"META:{info.Length}:{info.LastWriteTimeUtc.Ticks}:{(int)attributes}";
        }
        catch
        {
            return
                SettingsSnapshot.UnavailableRevision;
        }
    }

    private static long SafeLength(
        string path)
    {
        try
        {
            return
                new FileInfo(
                    path)
                    .Length;
        }
        catch
        {
            return
                -1;
        }
    }

    private static byte[] ComputeSha256(
        byte[] bytes)
    {
        return
            SHA256.HashData(
                bytes);
    }

    private static bool IsRecoverableFileException(
        Exception exception)
    {
        return exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException;
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
            // Temporary-file cleanup must not hide the original save result.
        }
    }

    private static AppSettings CreateDefaults()
    {
        return
            new AppSettings();
    }

    private sealed record SettingsSnapshot(
        string PrimaryRevision,
        byte[]? SourceBytes,
        bool SaveAllowed)
    {
        public const string UnavailableRevision =
            "UNAVAILABLE";
    }

    private sealed record SettingsFileState(
        bool Exists,
        bool SafeRegularFile,
        bool Valid,
        bool CanCompareRevision,
        string Revision,
        long Length,
        byte[]? Bytes,
        AppSettings? Settings)
    {
        public static SettingsFileState Missing { get; } =
            new(
                Exists:
                    false,
                SafeRegularFile:
                    true,
                Valid:
                    false,
                CanCompareRevision:
                    true,
                Revision:
                    "MISSING",
                Length:
                    0,
                Bytes:
                    null,
                Settings:
                    null);

        public static SettingsFileState Unavailable { get; } =
            new(
                Exists:
                    true,
                SafeRegularFile:
                    false,
                Valid:
                    false,
                CanCompareRevision:
                    false,
                Revision:
                    SettingsSnapshot.UnavailableRevision,
                Length:
                    -1,
                Bytes:
                    null,
                Settings:
                    null);
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
                // Defensive only: a lease should only exist while this thread
                // owns the mutex.
            }
        }
    }
}
