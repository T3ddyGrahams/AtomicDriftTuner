using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AppSettingsStore
{
    private const string DataDirectoryName =
        "AtomicDriftTuner";

    private const string SettingsFileName =
        "settings.json";

    private const int MaxSettingsFileBytes =
        1024 * 1024;

    // Every AppSettingsStore instance uses the same LocalAppData file.
    // Serialize access so one window/component cannot read the file while
    // another is replacing it.
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

    public AppSettingsStore()
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
                    SettingsFileName));

        EnsureDirectory();
    }

    public AppSettings Load()
    {
        lock (FileGate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return CreateDefaults();
                }

                var info =
                    new FileInfo(
                        _path);

                if (
                    info.Length <= 0 ||
                    info.Length > MaxSettingsFileBytes)
                {
                    return CreateDefaults();
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
                    if (
                        stream.Length <= 0 ||
                        stream.Length > MaxSettingsFileBytes)
                    {
                        return CreateDefaults();
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
                            return CreateDefaults();
                        }

                        offset +=
                            read;
                    }
                }

                var settings =
                    JsonSerializer.Deserialize<AppSettings>(
                        bytes,
                        Json);

                return settings
                    ?? CreateDefaults();
            }
            catch
            {
                // A corrupt or temporarily unreadable settings file should
                // never prevent ADT from starting.
                //
                // Leave the original file untouched so it remains available
                // for diagnostics or manual recovery.
                return CreateDefaults();
            }
        }
    }

    public void Save(
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(
            settings);

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
                ex is JsonException ||
                ex is NotSupportedException)
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
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
                .GetBytes(
                    json);
        }
        catch (EncoderFallbackException ex)
        {
            throw new InvalidDataException(
                "ADT application settings contained invalid text data.",
                ex);
        }

        if (bytes.Length > MaxSettingsFileBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to save a settings file larger than {MaxSettingsFileBytes:N0} bytes.");
        }

        lock (FileGate)
        {
            EnsureDirectory();

            WriteAtomically(
                bytes);
        }
    }

    private void WriteAtomically(
        byte[] bytes)
    {
        var temporaryPath =
            Path.Combine(
                _directory,
                $"{SettingsFileName}.{Guid.NewGuid():N}.tmp");

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

                // Ensure the complete temporary settings file is flushed
                // before it replaces the currently valid settings file.
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
                // A leftover .tmp file is harmless and is never considered
                // an ADT settings file. Do not hide the original save result
                // because cleanup failed.
            }
        }
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(
            _directory);
    }

    private static AppSettings CreateDefaults()
    {
        return new AppSettings();
    }
}
