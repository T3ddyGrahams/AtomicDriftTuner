using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class ShareCodeService
{
    public const string Schema = "atomic-share/v1";
    public const string Prefix = "AT1-";

    private const int MaxCompressedBytes = 32 * 1024;
    private const int MaxJsonBytes = 96 * 1024;
    private const int MaxPortableCodeChars = 2000;

    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public AtomicSharePayload Create(
        TuneInput input,
        TuneResult result,
        CarBehaviorTarget behavior)
    {
        behavior.Normalize();

        return new AtomicSharePayload
        {
            Schema = Schema,
            AtomicVersion = DistributionInfo.Version,
            CreatedUtc = DateTime.UtcNow,
            Input = new AtomicShareInput
            {
                Hardware = new AtomicShareHardware
                {
                    Id = input.Hardware.Id,
                    Manufacturer = input.Hardware.Manufacturer,
                    Model = input.Hardware.Model,
                    PeakTorqueNm = input.Hardware.PeakTorqueNm,
                    MaxRotationDeg = input.Hardware.MaxRotationDeg,
                    IsCustom = input.Hardware.IsCustom
                },
                Wheel = new AtomicShareWheel
                {
                    Id = input.Wheel.Id,
                    Manufacturer = input.Wheel.Manufacturer,
                    Model = input.Wheel.Model,
                    DiameterMm = input.Wheel.DiameterMm,
                    InertiaFactor = input.Wheel.InertiaFactor,
                    IsRound = input.Wheel.IsRound,
                    IsCustom = input.Wheel.IsCustom
                },
                Pack = new AtomicSharePack
                {
                    Id = input.DriftPack.Id,
                    Name = input.DriftPack.Name,
                    Category = input.DriftPack.Category,
                    GripBias = input.DriftPack.GripBias,
                    SelfSteerBias = input.DriftPack.SelfSteerBias,
                    DampingBias = input.DriftPack.DampingBias,
                    DetailBias = input.DriftPack.DetailBias,
                    IsCustom = input.DriftPack.IsCustom
                },
                Car = new AtomicShareCar
                {
                    Id = input.Car.Id,
                    PackId = input.Car.PackId,
                    DisplayName = input.Car.DisplayName,
                    MassKg = input.Car.MassKg,
                    PowerHp = input.Car.PowerHp,
                    TorqueNm = input.Car.TorqueNm,
                    Drivetrain = input.Car.Drivetrain,
                    SteeringLockPerSideDeg = input.Car.SteeringLockPerSideDeg,
                    CasterDeg = input.Car.CasterDeg,
                    FrontTireWidthMm = input.Car.FrontTireWidthMm,
                    RearTireWidthMm = input.Car.RearTireWidthMm,
                    Grip = input.Car.Grip,
                    IsCustom = input.Car.IsCustom,
                    SourceFolderName = CleanOptional(input.Car.SourceFolderName, 120)
                },
                Intent = new AtomicShareIntent
                {
                    Kind = input.Intent.Kind,
                    Name = input.Intent.Name
                }
            },
            Behavior = new AtomicShareBehavior
            {
                FrontEndBite = behavior.FrontEndBite,
                RearGrip = behavior.RearGrip,
                SelfSteerSpeed = behavior.SelfSteerSpeed,
                TransitionSpeed = behavior.TransitionSpeed,
                AngleStability = behavior.AngleStability,
                ThrottleSteering = behavior.ThrottleSteering,
                InitiationSharpness = behavior.InitiationSharpness
            },
            Recommendation = new AtomicShareRecommendation
            {
                Azom = new AtomicShareAzomRecommendation
                {
                    WheelRotationAngleDeg = result.Azom.Core.WheelRotationAngleDeg,
                    GameFfbStrengthPct = result.Azom.Core.GameFfbStrengthPct,
                    BaseTorqueOutputPct = result.Azom.Core.BaseTorqueOutputPct,
                    MaximumWheelSpeedPct = result.Azom.Core.MaximumWheelSpeedPct,
                    Interpolation = result.Azom.Core.Interpolation,
                    WheelDamperPct = result.Azom.WheelbaseEffects.WheelDamperPct,
                    WheelFrictionPct = result.Azom.WheelbaseEffects.WheelFrictionPct,
                    NaturalInertia = result.Azom.WheelbaseEffects.NaturalInertia,
                    HighSpeedDampingPct = result.Azom.HighSpeedDamping.DampingLevelPct,
                    HighSpeedTriggerKph = result.Azom.HighSpeedDamping.TriggerSpeedKph,
                    EqHz10 = result.Azom.FfbEqualizer.Hz10,
                    EqHz15 = result.Azom.FfbEqualizer.Hz15,
                    EqHz25 = result.Azom.FfbEqualizer.Hz25,
                    EqHz40 = result.Azom.FfbEqualizer.Hz40,
                    EqHz60 = result.Azom.FfbEqualizer.Hz60,
                    EqHz100 = result.Azom.FfbEqualizer.Hz100,
                    EqSensitivity = result.Azom.FfbEqualizer.Sensitivity,
                    OutputCurvePreset = result.Azom.FfbOutputCurve.Preset,
                    CurveNode20 = result.Azom.FfbOutputCurve.Node20,
                    CurveNode40 = result.Azom.FfbOutputCurve.Node40,
                    CurveNode60 = result.Azom.FfbOutputCurve.Node60,
                    CurveNode80 = result.Azom.FfbOutputCurve.Node80,
                    CurveNode100 = result.Azom.FfbOutputCurve.Node100
                },
                AssettoCorsa = new AssettoCorsaSettings
                {
                    GainPct = result.Ac.GainPct,
                    FilterPct = result.Ac.FilterPct,
                    MinimumForcePct = result.Ac.MinimumForcePct,
                    KerbPct = result.Ac.KerbPct,
                    RoadPct = result.Ac.RoadPct,
                    SlipPct = result.Ac.SlipPct,
                    AbsPct = result.Ac.AbsPct
                },
                EstimatedPeakWheelTorqueNm = result.EstimatedPeakWheelTorqueNm,
                SelfSteerScore = result.SelfSteerScore,
                StabilityScore = result.StabilityScore,
                DetailScore = result.DetailScore,
                Notes = result.Notes
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(4)
                    .Select(x => Clean(x, 240))
                    .ToList()
            }
        };
    }

    public string Encode(AtomicSharePayload payload)
    {
        Validate(payload);

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, _json);
        if (json.Length > MaxJsonBytes)
            throw new InvalidDataException("Share payload is too large.");

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(json, 0, json.Length);

        var compressed = output.ToArray();
        if (compressed.Length > MaxCompressedBytes)
            throw new InvalidDataException("Compressed share payload is too large.");

        var code = Prefix + ToBase64Url(compressed);
        if (code.Length > MaxPortableCodeChars)
            throw new InvalidDataException(
                $"Portable share code is {code.Length:N0} characters; v1 is limited to {MaxPortableCodeChars:N0}.");

        return code;
    }

    public AtomicSharePayload Decode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidDataException("Paste an Atomic Share Code first.");

        string compact = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());

        if (compact.Length > MaxPortableCodeChars)
            throw new InvalidDataException(
                $"Portable share code exceeds the {MaxPortableCodeChars:N0}-character v1 limit.");

        if (!compact.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Atomic Share Codes must start with {Prefix}");

        var encoded = compact[Prefix.Length..];
        byte[] compressed;
        try
        {
            compressed = FromBase64Url(encoded);
        }
        catch (FormatException)
        {
            throw new InvalidDataException("The share code contains invalid Base64URL data.");
        }

        if (compressed.Length == 0 || compressed.Length > MaxCompressedBytes)
            throw new InvalidDataException("The share code is empty or exceeds the supported size.");

        byte[] json;
        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            var buffer = new byte[8192];
            int total = 0;
            while (true)
            {
                int read = gzip.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                total += read;
                if (total > MaxJsonBytes)
                    throw new InvalidDataException("Decoded share payload exceeds the supported size.");

                output.Write(buffer, 0, read);
            }

            json = output.ToArray();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("The share code could not be decompressed.", ex);
        }

        AtomicSharePayload payload;
        try
        {
            payload =
                JsonSerializer.Deserialize<AtomicSharePayload>(json, _json)
                ?? throw new InvalidDataException("The share code contains no payload.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The share code contains invalid JSON.", ex);
        }

        Validate(payload);
        return payload;
    }

    public TuneInput ToTuneInput(AtomicSharePayload payload)
    {
        Validate(payload);

        return new TuneInput
        {
            Hardware = new HardwareProfile
            {
                Id = Clean(payload.Input.Hardware.Id, 120),
                Manufacturer = Clean(payload.Input.Hardware.Manufacturer, 80),
                Model = Clean(payload.Input.Hardware.Model, 120),
                PeakTorqueNm = payload.Input.Hardware.PeakTorqueNm,
                MaxRotationDeg = payload.Input.Hardware.MaxRotationDeg,
                IsCustom = payload.Input.Hardware.IsCustom
            },
            Wheel = new SteeringWheelProfile
            {
                Id = Clean(payload.Input.Wheel.Id, 120),
                Manufacturer = Clean(payload.Input.Wheel.Manufacturer, 80),
                Model = Clean(payload.Input.Wheel.Model, 120),
                DiameterMm = payload.Input.Wheel.DiameterMm,
                InertiaFactor = payload.Input.Wheel.InertiaFactor,
                IsRound = payload.Input.Wheel.IsRound,
                IsCustom = payload.Input.Wheel.IsCustom
            },
            DriftPack = new DriftPackProfile
            {
                Id = Clean(payload.Input.Pack.Id, 120),
                Name = Clean(payload.Input.Pack.Name, 160),
                Category = Clean(payload.Input.Pack.Category, 80),
                GripBias = payload.Input.Pack.GripBias,
                SelfSteerBias = payload.Input.Pack.SelfSteerBias,
                DampingBias = payload.Input.Pack.DampingBias,
                DetailBias = payload.Input.Pack.DetailBias,
                IsCustom = payload.Input.Pack.IsCustom
            },
            Car = new CarProfile
            {
                Id = Clean(payload.Input.Car.Id, 160),
                PackId = Clean(payload.Input.Car.PackId, 120),
                DisplayName = Clean(payload.Input.Car.DisplayName, 180),
                MassKg = payload.Input.Car.MassKg,
                PowerHp = payload.Input.Car.PowerHp,
                TorqueNm = payload.Input.Car.TorqueNm,
                Drivetrain = Clean(payload.Input.Car.Drivetrain, 20),
                SteeringLockPerSideDeg = payload.Input.Car.SteeringLockPerSideDeg,
                CasterDeg = payload.Input.Car.CasterDeg,
                FrontTireWidthMm = payload.Input.Car.FrontTireWidthMm,
                RearTireWidthMm = payload.Input.Car.RearTireWidthMm,
                Grip = payload.Input.Car.Grip,
                IsCustom = true,
                IsInstalled = false,
                SourceFolderName = CleanOptional(payload.Input.Car.SourceFolderName, 120),
                SourceFolderPath = null,
                Author = null,
                DataSourceSummary = "Imported Atomic Share Code; local path/data is not embedded.",
                Confidence = new CarDataConfidence()
            },
            Intent = new DriftIntent
            {
                Kind = payload.Input.Intent.Kind,
                Name = Clean(payload.Input.Intent.Name, 120)
            }
        };
    }

    public string BuildPreview(AtomicSharePayload payload)
    {
        Validate(payload);

        var a = payload.Recommendation.Azom;
        var ac = payload.Recommendation.AssettoCorsa;
        var b = payload.Behavior;

        var sb = new StringBuilder();
        sb.AppendLine($"{payload.Input.Car.DisplayName} • {payload.Input.Pack.Name}");
        sb.AppendLine($"{payload.Input.Hardware.Manufacturer} {payload.Input.Hardware.Model} • {payload.Input.Wheel.Model}");
        sb.AppendLine($"Target: {payload.Input.Intent.Name}");
        sb.AppendLine($"Created with Atomic {payload.AtomicVersion} • {payload.CreatedUtc:u}");
        sb.AppendLine();
        sb.AppendLine("SHARED RECOMMENDATION SNAPSHOT");
        sb.AppendLine($"Rotation {a.WheelRotationAngleDeg}° • Game FFB {a.GameFfbStrengthPct}% • Base Torque {a.BaseTorqueOutputPct}%");
        sb.AppendLine($"Max Wheel Speed {a.MaximumWheelSpeedPct}% • Interpolation {a.Interpolation}");
        sb.AppendLine($"Wheel Damper {a.WheelDamperPct}% • Friction {a.WheelFrictionPct}% • Natural Inertia {a.NaturalInertia}");
        sb.AppendLine($"High-Speed Damping {a.HighSpeedDampingPct}% @ {a.HighSpeedTriggerKph} kph");
        sb.AppendLine($"AC Gain {ac.GainPct}% • Filter {ac.FilterPct}% • Min Force {ac.MinimumForcePct}%");
        sb.AppendLine($"Scores: Self-Steer {payload.Recommendation.SelfSteerScore}/100 • Stability {payload.Recommendation.StabilityScore}/100 • Detail {payload.Recommendation.DetailScore}/100");
        sb.AppendLine($"Estimated peak wheel torque: {payload.Recommendation.EstimatedPeakWheelTorqueNm:0.0} Nm");
        sb.AppendLine();
        sb.AppendLine("DESIRED BEHAVIOR");
        sb.AppendLine($"Front {Signed(b.FrontEndBite)} • Rear Grip {Signed(b.RearGrip)} • Self-Steer {Signed(b.SelfSteerSpeed)} • Transition {Signed(b.TransitionSpeed)}");
        sb.AppendLine($"Angle Stability {Signed(b.AngleStability)} • Throttle Steering {Signed(b.ThrottleSteering)} • Initiation {Signed(b.InitiationSharpness)}");
        sb.AppendLine();
        sb.AppendLine("Import safety: context is loaded and regenerated locally. The snapshot above is never applied directly to AZOM.");
        return sb.ToString().TrimEnd();
    }

    private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

    private static void Validate(AtomicSharePayload payload)
    {
        if (payload is null)
            throw new InvalidDataException("Share payload is missing.");

        if (payload.Input is null ||
            payload.Input.Hardware is null ||
            payload.Input.Wheel is null ||
            payload.Input.Pack is null ||
            payload.Input.Car is null ||
            payload.Input.Intent is null ||
            payload.Behavior is null ||
            payload.Recommendation is null ||
            payload.Recommendation.Azom is null ||
            payload.Recommendation.AssettoCorsa is null)
        {
            throw new InvalidDataException("Share payload is missing required sections.");
        }

        if (!string.Equals(payload.Schema, Schema, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported share schema '{payload.Schema}'.");

        RequireText(payload.AtomicVersion, "Atomic version", 80);
        RequireText(payload.Input.Hardware.Id, "Hardware id", 120);
        RequireText(payload.Input.Hardware.Manufacturer, "Hardware manufacturer", 80);
        RequireText(payload.Input.Hardware.Model, "Hardware model", 120);
        RequireText(payload.Input.Wheel.Id, "Wheel id", 120);
        RequireText(payload.Input.Hardware.Model, "Hardware model", 120);
        RequireText(payload.Input.Wheel.Model, "Wheel model", 120);
        RequireText(payload.Input.Pack.Id, "Pack id", 120);
        RequireText(payload.Input.Pack.Name, "Pack name", 160);
        RequireText(payload.Input.Car.Id, "Car id", 160);
        RequireText(payload.Input.Car.PackId, "Car pack id", 120);
        RequireText(payload.Input.Car.DisplayName, "Car name", 180);

        if (!string.Equals(
                payload.Input.Car.PackId,
                payload.Input.Pack.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Share code car/pack identity does not match.");
        }
        RequireText(payload.Input.Intent.Name, "Drift target", 120);

        RequireRange(payload.Input.Hardware.PeakTorqueNm, 0.5, 60, "Peak torque");
        RequireRange(payload.Input.Hardware.MaxRotationDeg, 60, 3600, "Hardware rotation");
        RequireRange(payload.Input.Wheel.DiameterMm, 150, 650, "Wheel diameter");
        RequireRange(payload.Input.Wheel.InertiaFactor, 0.1, 5, "Wheel inertia");
        RequireRange(payload.Input.Pack.GripBias, -1, 1, "Pack grip bias");
        RequireRange(payload.Input.Pack.SelfSteerBias, -1, 1, "Pack self-steer bias");
        RequireRange(payload.Input.Pack.DampingBias, -1, 1, "Pack damping bias");
        RequireRange(payload.Input.Pack.DetailBias, -1, 1, "Pack detail bias");
        RequireRange(payload.Input.Car.MassKg, 250, 5000, "Car mass");
        RequireRange(payload.Input.Car.PowerHp, 20, 5000, "Car power");
        RequireRange(payload.Input.Car.TorqueNm, 0, 8000, "Car torque");
        RequireRange(payload.Input.Car.SteeringLockPerSideDeg, 5, 100, "Steering lock");
        RequireRange(payload.Input.Car.CasterDeg, -10, 30, "Caster");
        RequireRange(payload.Input.Car.FrontTireWidthMm, 80, 600, "Front tire width");
        RequireRange(payload.Input.Car.RearTireWidthMm, 80, 600, "Rear tire width");

        if (!Enum.IsDefined(payload.Input.Car.Grip))
            throw new InvalidDataException("Share code contains an unsupported grip level.");
        if (!Enum.IsDefined(payload.Input.Intent.Kind))
            throw new InvalidDataException("Share code contains an unsupported drift target.");
        if (!Enum.IsDefined(payload.Recommendation.Azom.OutputCurvePreset))
            throw new InvalidDataException("Share code contains an unsupported output-curve preset.");

        ValidateBehavior(payload.Behavior);

        var a = payload.Recommendation.Azom;
        RequireInt(a.WheelRotationAngleDeg, 60, 2700, "AZOM rotation");
        RequireInt(a.GameFfbStrengthPct, 0, 100, "Game FFB");
        RequireInt(a.BaseTorqueOutputPct, 50, 100, "Base torque");
        RequireInt(a.MaximumWheelSpeedPct, 0, 200, "Maximum wheel speed");
        RequireInt(a.Interpolation, 0, 10, "Interpolation");
        RequireInt(a.WheelDamperPct, 0, 100, "Wheel damper");
        RequireInt(a.WheelFrictionPct, 0, 100, "Wheel friction");
        RequireInt(a.NaturalInertia, 100, 500, "Natural inertia");
        RequireInt(a.HighSpeedDampingPct, 0, 100, "High-speed damping");
        RequireInt(a.HighSpeedTriggerKph, 0, 400, "High-speed trigger");

        foreach (var eq in new[] { a.EqHz10, a.EqHz15, a.EqHz25, a.EqHz40, a.EqHz60, a.EqHz100 })
            RequireInt(eq, 0, 400, "EQ value");
        RequireInt(a.EqSensitivity, 0, 10, "EQ sensitivity");
        foreach (var node in new[] { a.CurveNode20, a.CurveNode40, a.CurveNode60, a.CurveNode80, a.CurveNode100 })
            RequireInt(node, 0, 100, "Output curve node");

        var ac = payload.Recommendation.AssettoCorsa;
        RequireInt(ac.GainPct, 0, 200, "AC gain");
        RequireInt(ac.FilterPct, 0, 100, "AC filter");
        RequireInt(ac.MinimumForcePct, 0, 100, "AC minimum force");
        RequireInt(ac.KerbPct, 0, 100, "AC kerb");
        RequireInt(ac.RoadPct, 0, 100, "AC road");
        RequireInt(ac.SlipPct, 0, 100, "AC slip");
        RequireInt(ac.AbsPct, 0, 100, "AC ABS");

        RequireRange(payload.Recommendation.EstimatedPeakWheelTorqueNm, 0, 100, "Estimated peak torque");
        RequireInt(payload.Recommendation.SelfSteerScore, 0, 100, "Self-steer score");
        RequireInt(payload.Recommendation.StabilityScore, 0, 100, "Stability score");
        RequireInt(payload.Recommendation.DetailScore, 0, 100, "Detail score");

        payload.Recommendation.Notes ??= [];

        if (payload.Recommendation.Notes.Count > 4)
            throw new InvalidDataException("Share code contains too many notes.");

        foreach (var note in payload.Recommendation.Notes)
            RequireText(note, "Tune note", 240, allowEmpty: false);
    }

    private static void ValidateBehavior(AtomicShareBehavior b)
    {
        foreach (var value in new[]
        {
            b.FrontEndBite, b.RearGrip, b.SelfSteerSpeed, b.TransitionSpeed,
            b.AngleStability, b.ThrottleSteering, b.InitiationSharpness
        })
        {
            RequireInt(value, -2, 2, "Desired Behavior");
        }
    }

    private static void RequireText(string? value, string label, int maxLength, bool allowEmpty = false)
    {
        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{label} is missing.");

        if (value is not null && value.Length > maxLength)
            throw new InvalidDataException($"{label} is too long.");
    }

    private static void RequireRange(double value, double min, double max, string label)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"{label} is outside the supported share-code range.");
    }

    private static void RequireInt(int value, int min, int max, string label)
    {
        if (value < min || value > max)
            throw new InvalidDataException($"{label} is outside the supported share-code range.");
    }

    private static string Clean(string? value, int maxLength)
    {
        var cleaned = (value ?? "").Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private static string? CleanOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Clean(value, maxLength);
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value
            .Replace('-', '+')
            .Replace('_', '/');

        base64 += (base64.Length % 4) switch
        {
            0 => "",
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Invalid Base64URL length.")
        };

        return Convert.FromBase64String(base64);
    }
}
