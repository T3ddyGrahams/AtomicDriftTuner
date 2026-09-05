using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class ProfileStore
{
    private const int MaxProfileFileBytes =
        4 * 1024 * 1024;

    private static readonly object FileGate =
        new();

    private static readonly JsonSerializerOptions Json =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public void Save(
        SavedTune tune,
        string path)
    {
        ArgumentNullException.ThrowIfNull(
            tune);

        var normalizedPath =
            NormalizePath(
                path);

        var directory =
            Path.GetDirectoryName(
                normalizedPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "ADT could not determine the destination folder for the tune profile.");
        }

        string json;

        try
        {
            json =
                JsonSerializer.Serialize(
                    tune,
                    Json);
        }
        catch (Exception ex)
            when (
                ex is JsonException ||
                ex is NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not serialize the tune profile.",
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
                "ADT tune profile contained invalid text data.",
                ex);
        }

        if (bytes.Length > MaxProfileFileBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to save a tune profile larger than {MaxProfileFileBytes:N0} bytes.");
        }

        lock (FileGate)
        {
            Directory.CreateDirectory(
                directory);

            WriteAtomically(
                normalizedPath,
                directory,
                bytes);
        }
    }

    public SavedTune Load(
        string path)
    {
        var normalizedPath =
            NormalizePath(
                path);

        lock (FileGate)
        {
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException(
                    "ADT tune profile was not found.",
                    normalizedPath);
            }

            byte[] bytes;

            try
            {
                var info =
                    new FileInfo(
                        normalizedPath);

                if (
                    info.Length <= 0 ||
                    info.Length > MaxProfileFileBytes)
                {
                    throw new InvalidDataException(
                        "ADT tune profile has an invalid file size.");
                }

                using var stream =
                    new FileStream(
                        normalizedPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        32 * 1024,
                        FileOptions.SequentialScan);

                if (
                    stream.Length <= 0 ||
                    stream.Length > MaxProfileFileBytes)
                {
                    throw new InvalidDataException(
                        "ADT tune profile has an invalid file size.");
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
                            "ADT tune profile ended unexpectedly while being read.");
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
                    "ADT could not read the tune profile.",
                    ex);
            }

            SavedTune? tune;

            try
            {
                tune =
                    JsonSerializer.Deserialize<SavedTune>(
                        bytes,
                        Json);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    "ADT tune profile contains invalid JSON.",
                    ex);
            }
            catch (NotSupportedException ex)
            {
                throw new InvalidDataException(
                    "ADT tune profile contains unsupported data.",
                    ex);
            }

            if (tune is null)
            {
                throw new InvalidDataException(
                    "ADT tune profile is empty or invalid.");
            }

            ValidateLoadedTune(
                tune);

            return tune;
        }
    }

    private static void WriteAtomically(
        string destinationPath,
        string directory,
        byte[] bytes)
    {
        var fileName =
            Path.GetFileName(
                destinationPath);

        var temporaryPath =
            Path.Combine(
                directory,
                $"{fileName}.{Guid.NewGuid():N}.tmp");

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
                destinationPath,
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
                // A leftover temporary file must not hide the original save
                // result. ADT never treats *.tmp files as saved profiles.
            }
        }
    }

    private static string NormalizePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Tune profile path is required.",
                nameof(path));
        }

        try
        {
            var expanded =
                Environment.ExpandEnvironmentVariables(
                    path.Trim().Trim('"'));

            if (string.IsNullOrWhiteSpace(expanded))
            {
                throw new ArgumentException(
                    "Tune profile path is required.",
                    nameof(path));
            }

            return Path.GetFullPath(
                expanded);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
            when (
                ex is NotSupportedException ||
                ex is PathTooLongException)
        {
            throw new ArgumentException(
                "Tune profile path is invalid.",
                nameof(path),
                ex);
        }
    }

    private static void ValidateLoadedTune(
        SavedTune tune)
    {
        // Keep this deliberately conservative.
        //
        // ProfileStore should reject structurally unusable files without
        // inventing assumptions about tune-model fields that may evolve
        // between ADT releases.
        var type =
            tune.GetType();

        if (type != typeof(SavedTune))
        {
            throw new InvalidDataException(
                "ADT tune profile has an unexpected data type.");
        }
    }
}
