using System.Globalization;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class TelemetrySessionStore
{
    private const string DataDirectoryName =
        "AtomicDriftTuner";

    private const string TelemetryDirectoryName =
        "TelemetrySessions";

    private const string SessionFileName =
        "session.json";

    private const string CsvFileName =
        "telemetry.csv";

    private const long MaxSessionJsonBytes =
        64L * 1024L * 1024L;

    private const int MaxCarFolderNameLength =
        100;

    private const int DefaultRecentCount =
        30;

    private const int MaximumRecentCount =
        500;

    private static readonly object FileGate =
        new();

    private static readonly JsonSerializerOptions Json =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public string RootDirectory { get; }

    public TelemetrySessionStore()
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                "Windows did not provide a LocalAppData folder for ADT.");
        }

        RootDirectory =
            Path.GetFullPath(
                Path.Combine(
                    localAppData,
                    DataDirectoryName,
                    TelemetryDirectoryName));
    }

    public (
        string JsonPath,
        string CsvPath)
        Save(
            TelemetrySession session,
            TelemetryAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        ArgumentNullException.ThrowIfNull(
            analysis);

        lock (FileGate)
        {
            Directory.CreateDirectory(
                RootDirectory);

            var folder =
                CreateSessionDirectory(
                    session);

            var jsonPath =
                Path.Combine(
                    folder,
                    SessionFileName);

            var csvPath =
                Path.Combine(
                    folder,
                    CsvFileName);

            try
            {
                WriteSessionJson(
                    jsonPath,
                    session,
                    analysis);

                WriteTelemetryCsv(
                    csvPath,
                    session);

                return (
                    jsonPath,
                    csvPath);
            }
            catch
            {
                // If this save created a brand-new session directory and
                // failed before completing both files, remove the incomplete
                // session so ADT does not later treat it as a valid run.
                TryDeleteIncompleteSessionDirectory(
                    folder);

                throw;
            }
        }
    }

    public List<SavedTelemetrySession> ListRecent(
        TuneInput input,
        int maxCount = DefaultRecentCount)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        var requestedCount =
            Math.Clamp(
                maxCount,
                1,
                MaximumRecentCount);

        if (!Directory.Exists(
                RootDirectory))
        {
            return [];
        }

        var candidates =
            EnumerateSessionFiles();

        var result =
            new List<SavedTelemetrySession>();

        foreach (var candidate in candidates)
        {
            var saved =
                TryLoad(
                    candidate.Path);

            if (saved is null)
            {
                continue;
            }

            if (!MatchesInput(
                    saved.Session,
                    input))
            {
                continue;
            }

            result.Add(
                saved);

            if (result.Count >= requestedCount)
            {
                break;
            }
        }

        return result;
    }

    public SavedTelemetrySession? TryLoad(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalizedPath;

        try
        {
            normalizedPath =
                Path.GetFullPath(
                    path.Trim().Trim('"'));
        }
        catch
        {
            return null;
        }

        try
        {
            if (!File.Exists(
                    normalizedPath))
            {
                return null;
            }

            var info =
                new FileInfo(
                    normalizedPath);

            if (
                info.Length <= 0 ||
                info.Length > MaxSessionJsonBytes)
            {
                return null;
            }

            byte[] bytes;

            using (
                var stream =
                    new FileStream(
                        normalizedPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        64 * 1024,
                        FileOptions.SequentialScan))
            {
                if (
                    stream.Length <= 0 ||
                    stream.Length > MaxSessionJsonBytes)
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

            using var document =
                JsonDocument.Parse(
                    bytes);

            var root =
                document.RootElement;

            if (
                root.ValueKind != JsonValueKind.Object ||
                !TryGetCaseInsensitive(
                    root,
                    "session",
                    out var sessionElement))
            {
                return null;
            }

            TelemetrySession? session;

            try
            {
                session =
                    sessionElement.Deserialize<TelemetrySession>(
                        Json);
            }
            catch (
                Exception ex)
                when (
                    ex is JsonException ||
                    ex is NotSupportedException)
            {
                return null;
            }

            if (session is null)
            {
                return null;
            }

            // Always analyze the stored raw samples again using the current
            // ADT analyzer.
            //
            // This deliberately does not trust serialized derived analysis
            // from older releases. Historical sessions therefore benefit
            // from newer analysis logic while preserving the original raw
            // telemetry.
            TelemetryAnalysis analysis;

            try
            {
                analysis =
                    new TelemetryAnalyzer()
                        .Analyze(
                            session);
            }
            catch
            {
                // A session whose raw telemetry cannot be analyzed by the
                // current engine should not prevent other sessions loading.
                return null;
            }

            return new SavedTelemetrySession
            {
                JsonPath =
                    normalizedPath,

                Session =
                    session,

                Analysis =
                    analysis
            };
        }
        catch
        {
            // Damaged, obsolete, inaccessible, or concurrently removed
            // telemetry sessions are ignored individually. One bad run must
            // never prevent ADT from loading the rest of the history.
            return null;
        }
    }

    private string CreateSessionDirectory(
        TelemetrySession session)
    {
        var startedUtc =
            NormalizeSessionTime(
                session.StartedUtc);

        var stamp =
            startedUtc
                .ToLocalTime()
                .ToString(
                    "yyyyMMdd_HHmmss",
                    CultureInfo.InvariantCulture);

        var safeCar =
            SafeFileNamePart(
                session.CarName);

        var baseName =
            $"{stamp}_{safeCar}";

        var sessionSuffix =
            BuildSessionSuffix(
                session.Id);

        for (var attempt = 0; attempt < 1000; attempt++)
        {
            string directoryName;

            if (attempt == 0)
            {
                directoryName =
                    baseName;
            }
            else if (attempt == 1 &&
                     !string.IsNullOrWhiteSpace(sessionSuffix))
            {
                directoryName =
                    $"{baseName}_{sessionSuffix}";
            }
            else
            {
                directoryName =
                    $"{baseName}_{sessionSuffix}_{attempt:D3}";
            }

            var candidate =
                Path.Combine(
                    RootDirectory,
                    directoryName);

            try
            {
                Directory.CreateDirectory(
                    candidate);

                // Directory.CreateDirectory succeeds for an existing folder,
                // so use the absence of ADT session files to determine
                // whether this candidate is available.
                if (
                    !File.Exists(
                        Path.Combine(
                            candidate,
                            SessionFileName)) &&
                    !File.Exists(
                        Path.Combine(
                            candidate,
                            CsvFileName)))
                {
                    return candidate;
                }
            }
            catch (IOException)
            {
                // Try the next unique suffix.
            }
        }

        throw new IOException(
            "ADT could not create a unique telemetry-session directory.");
    }

    private static DateTime NormalizeSessionTime(
        DateTime startedUtc)
    {
        if (startedUtc == default)
        {
            return DateTime.UtcNow;
        }

        return startedUtc.Kind switch
        {
            DateTimeKind.Utc =>
                startedUtc,

            DateTimeKind.Local =>
                startedUtc.ToUniversalTime(),

            _ =>
                DateTime.SpecifyKind(
                    startedUtc,
                    DateTimeKind.Utc)
        };
    }

    private static string BuildSessionSuffix(
        string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(
                sessionId))
        {
            return Guid.NewGuid()
                .ToString("N")[..6];
        }

        var cleaned =
            new string(
                sessionId
                    .Where(
                        char.IsLetterOrDigit)
                    .ToArray());

        if (string.IsNullOrWhiteSpace(
                cleaned))
        {
            return Guid.NewGuid()
                .ToString("N")[..6];
        }

        return cleaned[
            ..Math.Min(
                8,
                cleaned.Length)];
    }

    private static void WriteSessionJson(
        string destinationPath,
        TelemetrySession session,
        TelemetryAnalysis analysis)
    {
        var envelope =
            new
            {
                session,
                analysis
            };

        byte[] bytes;

        try
        {
            bytes =
                JsonSerializer.SerializeToUtf8Bytes(
                    envelope,
                    Json);
        }
        catch (Exception ex)
            when (
                ex is JsonException ||
                ex is NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not serialize the telemetry session.",
                ex);
        }

        if (bytes.Length > MaxSessionJsonBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to save a telemetry session larger than {MaxSessionJsonBytes:N0} bytes.");
        }

        WriteBytesAtomically(
            destinationPath,
            bytes);
    }

    private static void WriteTelemetryCsv(
        string destinationPath,
        TelemetrySession session)
    {
        var directory =
            Path.GetDirectoryName(
                destinationPath)
            ?? throw new InvalidOperationException(
                "ADT could not determine the telemetry CSV directory.");

        var temporaryPath =
            Path.Combine(
                directory,
                $"{CsvFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (
                var stream =
                    new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        64 * 1024,
                        FileOptions.WriteThrough))
            using (
                var writer =
                    new StreamWriter(
                        stream,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(
                    "time_s,speed_kmh,throttle,brake,clutch,gear,rpm,steer_angle,steer_rate_deg_s,slip_angle_deg,yaw_rate_deg_s,lat_g,long_g,front_wheel_slip,rear_wheel_slip,final_ff,front_pressure,rear_pressure");

                foreach (var sample in session.Samples)
                {
                    writer.WriteLine(
                        string.Join(
                            ',',
                            new[]
                            {
                                FormatNumber(
                                    sample.TimeSeconds),

                                FormatNumber(
                                    sample.SpeedKmh),

                                FormatNumber(
                                    sample.Throttle),

                                FormatNumber(
                                    sample.Brake),

                                FormatNumber(
                                    sample.Clutch),

                                sample.Gear.ToString(
                                    CultureInfo.InvariantCulture),

                                sample.Rpm.ToString(
                                    CultureInfo.InvariantCulture),

                                FormatNumber(
                                    sample.SteeringAngleDeg),

                                FormatNumber(
                                    sample.SteeringRateDegPerSec),

                                FormatNumber(
                                    sample.SlipAngleDeg),

                                FormatNumber(
                                    sample.YawRateDegPerSec),

                                FormatNumber(
                                    sample.LateralG),

                                FormatNumber(
                                    sample.LongitudinalG),

                                FormatNumber(
                                    sample.FrontWheelSlipAvg),

                                FormatNumber(
                                    sample.RearWheelSlipAvg),

                                FormatNumber(
                                    sample.FinalFfb),

                                FormatNumber(
                                    sample.FrontTyrePressureAvg),

                                FormatNumber(
                                    sample.RearTyrePressureAvg)
                            }));
                }

                writer.Flush();

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
            TryDeleteFile(
                temporaryPath);
        }
    }

    private static void WriteBytesAtomically(
        string destinationPath,
        byte[] bytes)
    {
        var directory =
            Path.GetDirectoryName(
                destinationPath)
            ?? throw new InvalidOperationException(
                "ADT could not determine the telemetry-session directory.");

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
                        64 * 1024,
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
            TryDeleteFile(
                temporaryPath);
        }
    }

    private IEnumerable<SessionFileCandidate> EnumerateSessionFiles()
    {
        IEnumerable<string> paths;

        try
        {
            var options =
                new EnumerationOptions
                {
                    RecurseSubdirectories =
                        true,

                    IgnoreInaccessible =
                        true,

                    ReturnSpecialDirectories =
                        false,

                    AttributesToSkip =
                        FileAttributes.ReparsePoint
                };

            paths =
                Directory.EnumerateFiles(
                    RootDirectory,
                    SessionFileName,
                    options);
        }
        catch
        {
            yield break;
        }

        var candidates =
            new List<SessionFileCandidate>();

        try
        {
            foreach (var path in paths)
            {
                try
                {
                    candidates.Add(
                        new SessionFileCandidate(
                            Path:
                                path,

                            LastWriteUtc:
                                File.GetLastWriteTimeUtc(
                                    path)));
                }
                catch
                {
                    // File may have disappeared or become inaccessible while
                    // the telemetry history was being enumerated.
                }
            }
        }
        catch
        {
            // Enumeration itself may fail if folders change concurrently.
            // Return whichever valid candidates were already discovered.
        }

        foreach (
            var candidate in candidates
                .OrderByDescending(
                    x => x.LastWriteUtc))
        {
            yield return candidate;
        }
    }

    private static bool MatchesInput(
        TelemetrySession session,
        TuneInput input)
    {
        if (
            input.Car is null ||
            input.DriftPack is null ||
            input.Hardware is null ||
            input.Wheel is null)
        {
            return false;
        }

        var sessionCarFolder =
            NormalizeComparisonValue(
                session.CarFolder);

        var inputCarFolder =
            NormalizeComparisonValue(
                input.Car.SourceFolderName);

        bool carMatches;

        if (
            sessionCarFolder is not null &&
            inputCarFolder is not null)
        {
            carMatches =
                string.Equals(
                    sessionCarFolder,
                    inputCarFolder,
                    StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var sessionCarName =
                NormalizeComparisonValue(
                    session.CarName);

            var inputCarName =
                NormalizeComparisonValue(
                    input.Car.DisplayName);

            carMatches =
                sessionCarName is not null &&
                inputCarName is not null &&
                string.Equals(
                    sessionCarName,
                    inputCarName,
                    StringComparison.OrdinalIgnoreCase);
        }

        if (!carMatches)
        {
            return false;
        }

        var packMatches =
            OptionalMatch(
                session.DriftPack,
                input.DriftPack.Name);

        var baseMatches =
            OptionalMatch(
                session.Wheelbase,
                input.Hardware.Model);

        var wheelMatches =
            OptionalMatch(
                session.SteeringWheel,
                input.Wheel.Model);

        return
            packMatches &&
            baseMatches &&
            wheelMatches;
    }

    private static bool OptionalMatch(
        string? storedValue,
        string? requestedValue)
    {
        var stored =
            NormalizeComparisonValue(
                storedValue);

        if (stored is null)
        {
            // Historical telemetry may predate this identity field.
            return true;
        }

        var requested =
            NormalizeComparisonValue(
                requestedValue);

        return
            requested is not null &&
            string.Equals(
                stored,
                requested,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeComparisonValue(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static bool TryGetCaseInsensitive(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value =
                default;

            return false;
        }

        foreach (
            var property in element.EnumerateObject())
        {
            if (property.Name.Equals(
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

    private static string FormatNumber(
        double value)
    {
        return value.ToString(
            "0.######",
            CultureInfo.InvariantCulture);
    }

    private static string SafeFileNamePart(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return "car";
        }

        var invalid =
            Path.GetInvalidFileNameChars()
                .ToHashSet();

        var builder =
            new StringBuilder();

        foreach (var character in value.Trim())
        {
            if (
                invalid.Contains(character) ||
                char.IsControl(character))
            {
                builder.Append(
                    '_');
            }
            else
            {
                builder.Append(
                    character);
            }

            if (builder.Length >= MaxCarFolderNameLength)
            {
                break;
            }
        }

        var result =
            builder
                .ToString()
                .Trim()
                .TrimEnd('.', ' ');

        return string.IsNullOrWhiteSpace(result)
            ? "car"
            : result;
    }

    private static void TryDeleteIncompleteSessionDirectory(
        string folder)
    {
        try
        {
            if (!Directory.Exists(
                    folder))
            {
                return;
            }

            // Only delete the folder created for this failed save. Session
            // folders are not shared between runs.
            Directory.Delete(
                folder,
                recursive: true);
        }
        catch
        {
            // Cleanup failure must not hide the original save exception.
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
            // Temporary-file cleanup must never replace the real exception.
        }
    }

    private sealed record SessionFileCandidate(
        string Path,
        DateTime LastWriteUtc);
}
