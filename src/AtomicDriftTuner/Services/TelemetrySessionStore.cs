using System.Globalization;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Engine;

namespace AtomicDriftTuner.Services;

public sealed class TelemetrySessionStore
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AtomicDriftTuner",
        "TelemetrySessions");

    public (string JsonPath, string CsvPath) Save(
        TelemetrySession session,
        TelemetryAnalysis analysis)
    {
        Directory.CreateDirectory(RootDirectory);

        string stamp =
            session.StartedUtc
                .ToLocalTime()
                .ToString("yyyyMMdd_HHmmss");

        string safeCar = Safe(session.CarName);

        string folder =
            Path.Combine(
                RootDirectory,
                $"{stamp}_{safeCar}");

        // Avoid overwriting an earlier run started within the same second.
        if (Directory.Exists(folder))
            folder += "_" + session.Id[..Math.Min(6, session.Id.Length)];

        Directory.CreateDirectory(folder);

        string jsonPath =
            Path.Combine(
                folder,
                "session.json");

        var envelope = new
        {
            session,
            analysis
        };

        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(
                envelope,
                _json));

        string csvPath =
            Path.Combine(
                folder,
                "telemetry.csv");

        using var w =
            new StreamWriter(
                csvPath,
                false,
                new UTF8Encoding(false));

        w.WriteLine(
            "time_s,speed_kmh,throttle,brake,clutch,gear,rpm,steer_angle,steer_rate_deg_s,slip_angle_deg,yaw_rate_deg_s,lat_g,long_g,front_wheel_slip,rear_wheel_slip,final_ff,front_pressure,rear_pressure");

        foreach (var x in session.Samples)
        {
            w.WriteLine(
                string.Join(
                    ',',
                    new[]
                    {
                        F(x.TimeSeconds),
                        F(x.SpeedKmh),
                        F(x.Throttle),
                        F(x.Brake),
                        F(x.Clutch),
                        x.Gear.ToString(CultureInfo.InvariantCulture),
                        x.Rpm.ToString(CultureInfo.InvariantCulture),
                        F(x.SteeringAngleDeg),
                        F(x.SteeringRateDegPerSec),
                        F(x.SlipAngleDeg),
                        F(x.YawRateDegPerSec),
                        F(x.LateralG),
                        F(x.LongitudinalG),
                        F(x.FrontWheelSlipAvg),
                        F(x.RearWheelSlipAvg),
                        F(x.FinalFfb),
                        F(x.FrontTyrePressureAvg),
                        F(x.RearTyrePressureAvg)
                    }));
        }

        return (jsonPath, csvPath);
    }

    public List<SavedTelemetrySession> ListRecent(
        TuneInput input,
        int maxCount = 30)
    {
        var result = new List<SavedTelemetrySession>();

        if (!Directory.Exists(RootDirectory))
            return result;

        foreach (var path in Directory
                     .EnumerateFiles(
                         RootDirectory,
                         "session.json",
                         SearchOption.AllDirectories)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var saved = TryLoad(path);
            if (saved is null)
                continue;

            if (!MatchesInput(saved.Session, input))
                continue;

            result.Add(saved);

            if (result.Count >= Math.Max(1, maxCount))
                break;
        }

        return result;
    }

    public SavedTelemetrySession? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var document =
                JsonDocument.Parse(
                    File.ReadAllText(path));

            var root = document.RootElement;

            if (!TryGetCaseInsensitive(root, "session", out var sessionElement) ||
                !TryGetCaseInsensitive(root, "analysis", out var analysisElement))
                return null;

            var session =
                sessionElement.Deserialize<TelemetrySession>(_json);

            if (session is null)
                return null;

            // Always re-run the current analyzer from the stored raw samples.
            // This keeps older v0.4/v0.6 session files usable when v0.7 adds
            // new derived metrics such as FFB clipping/headroom.
            var analysis =
                new TelemetryAnalyzer()
                    .Analyze(session);

            return new SavedTelemetrySession
            {
                JsonPath = path,
                Session = session,
                Analysis = analysis
            };
        }
        catch
        {
            // A damaged/old telemetry file should not prevent the assistant
            // from loading other valid sessions.
            return null;
        }
    }

    private static bool MatchesInput(
        TelemetrySession session,
        TuneInput input)
    {
        bool carMatches;

        if (!string.IsNullOrWhiteSpace(session.CarFolder) &&
            !string.IsNullOrWhiteSpace(input.Car.SourceFolderName))
        {
            carMatches =
                session.CarFolder.Equals(
                    input.Car.SourceFolderName,
                    StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            carMatches =
                session.CarName.Equals(
                    input.Car.DisplayName,
                    StringComparison.OrdinalIgnoreCase);
        }

        if (!carMatches)
            return false;

        bool packMatches =
            string.IsNullOrWhiteSpace(session.DriftPack) ||
            session.DriftPack.Equals(
                input.DriftPack.Name,
                StringComparison.OrdinalIgnoreCase);

        bool baseMatches =
            string.IsNullOrWhiteSpace(session.Wheelbase) ||
            session.Wheelbase.Equals(
                input.Hardware.Model,
                StringComparison.OrdinalIgnoreCase);

        bool wheelMatches =
            string.IsNullOrWhiteSpace(session.SteeringWheel) ||
            session.SteeringWheel.Equals(
                input.Wheel.Model,
                StringComparison.OrdinalIgnoreCase);

        return packMatches && baseMatches && wheelMatches;
    }

    private static bool TryGetCaseInsensitive(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string F(double x) =>
        x.ToString(
            "0.######",
            CultureInfo.InvariantCulture);

    private static string Safe(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');

        return string.IsNullOrWhiteSpace(value)
            ? "car"
            : value.Trim();
    }
}
