using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AzomRevertStore
{
    private const string DataDirectoryName =
        "AtomicDriftTuner";

    private const string RevertFileName =
        "azom-last-apply-backup.json";

    private const int MaxRevertFileBytes =
        512 * 1024;

    private const int MaxChangedProperties =
        128;

    private const int MaxPropertyNameLength =
        256;

    // All AzomRevertStore instances in this ADT process point to the same
    // LocalAppData file. Serialize access so one controller cannot read while
    // another controller is replacing the backup.
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

    public AzomRevertStore()
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
                    RevertFileName));

        EnsureDirectory();
    }

    public void Save(
        AzomLiveSnapshot snapshot,
        IEnumerable<string> changedProperties)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changedProperties);

        var properties =
            NormalizeChangedProperties(
                changedProperties);

        if (properties.Count == 0)
        {
            throw new ArgumentException(
                "ADT cannot create an AZOM revert record without at least one changed AZOM property.",
                nameof(changedProperties));
        }

        var record =
            new AzomRevertRecord
            {
                Snapshot =
                    snapshot,

                ChangedProperties =
                    properties
            };

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
                ex is JsonException ||
                ex is NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not serialize the AZOM revert record.",
                ex);
        }

        var bytes =
            new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
                .GetBytes(json);

        if (bytes.Length > MaxRevertFileBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to save an AZOM revert record larger than {MaxRevertFileBytes:N0} bytes.");
        }

        lock (FileGate)
        {
            EnsureDirectory();

            WriteAtomically(
                bytes);
        }
    }

    public AzomRevertRecord? Load()
    {
        lock (FileGate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }

                var info =
                    new FileInfo(
                        _path);

                if (
                    info.Length <= 0 ||
                    info.Length > MaxRevertFileBytes)
                {
                    return null;
                }

                byte[] bytes;

                using (
                    var stream =
                        new FileStream(
                            _path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            16 * 1024,
                            FileOptions.SequentialScan))
                {
                    if (stream.Length > MaxRevertFileBytes)
                    {
                        return null;
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
                            return null;
                        }

                        offset +=
                            read;
                    }
                }

                var record =
                    JsonSerializer.Deserialize<AzomRevertRecord>(
                        bytes,
                        Json);

                return ValidateLoadedRecord(
                    record)
                    ? record
                    : null;
            }
            catch
            {
                // A damaged or unreadable backup must never crash ADT or cause
                // an unvalidated rollback attempt. Leave the file untouched so
                // it remains available for diagnostics/manual recovery.
                return null;
            }
        }
    }

    private void WriteAtomically(
        byte[] bytes)
    {
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

                // Make a best effort to push the completed temporary file to
                // disk before replacing the current rollback record.
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
                // Temporary-file cleanup failure must not hide the real save
                // result. A leftover .tmp file is never treated as a rollback.
            }
        }
    }

    private static List<string> NormalizeChangedProperties(
        IEnumerable<string> changedProperties)
    {
        var result =
            new List<string>();

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var property in changedProperties)
        {
            if (string.IsNullOrWhiteSpace(property))
            {
                continue;
            }

            var normalized =
                property.Trim();

            if (
                normalized.Length > MaxPropertyNameLength ||
                !normalized.StartsWith(
                    "AZOM.",
                    StringComparison.Ordinal) ||
                normalized.Contains('\r') ||
                normalized.Contains('\n') ||
                normalized.Contains('\0'))
            {
                throw new ArgumentException(
                    $"Invalid AZOM revert property name: '{normalized}'.",
                    nameof(changedProperties));
            }

            if (seen.Add(normalized))
            {
                result.Add(
                    normalized);
            }

            if (result.Count > MaxChangedProperties)
            {
                throw new ArgumentException(
                    $"An AZOM revert record cannot contain more than {MaxChangedProperties} changed properties.",
                    nameof(changedProperties));
            }
        }

        return result;
    }

    private static bool ValidateLoadedRecord(
        AzomRevertRecord? record)
    {
        if (
            record is null ||
            record.Snapshot is null ||
            record.ChangedProperties is null ||
            record.ChangedProperties.Count == 0 ||
            record.ChangedProperties.Count > MaxChangedProperties)
        {
            return false;
        }

        if (
            !string.Equals(
                record.Snapshot.PropertyNamespace,
                "AZOM",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var property in record.ChangedProperties)
        {
            if (string.IsNullOrWhiteSpace(property))
            {
                return false;
            }

            var normalized =
                property.Trim();

            if (
                normalized.Length > MaxPropertyNameLength ||
                !normalized.StartsWith(
                    "AZOM.",
                    StringComparison.Ordinal) ||
                normalized.Contains('\r') ||
                normalized.Contains('\n') ||
                normalized.Contains('\0'))
            {
                return false;
            }

            if (!seen.Add(normalized))
            {
                return false;
            }
        }

        return true;
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(
            _directory);
    }
}
