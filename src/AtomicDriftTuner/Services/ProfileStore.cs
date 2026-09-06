using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Reads and writes user-selected ADT tune profiles.
///
/// Saved tune files are portable data, not trusted executable state. ProfileStore
/// validates the file envelope, tuning context, recommendation snapshot, and
/// optional calibration before returning anything to the UI.
/// </summary>
public sealed class ProfileStore
{
    private const int MaxProfileFileBytes =
        4 * 1024 * 1024;

    private const int MaxIdLength =
        256;

    private const int MaxShortTextLength =
        512;

    private const int MaxPathTextLength =
        4096;

    private const int MaxSummaryTextLength =
        4096;

    private const int MaxNotes =
        256;

    private const int MaxNoteLength =
        8192;

    // Broad corruption/abuse sanity bounds for user-supplied profile context.
    // These are intentionally much wider than normal drift-car hardware values;
    // they are not tuning recommendations.
    private const double MaxPeakTorqueNm =
        1000;

    private const int MaxWheelbaseRotationDeg =
        100_000;

    private const double MaxWheelDiameterMm =
        2000;

    private const double MaxWheelInertiaFactor =
        100;

    private const double MaxProfileBiasMagnitude =
        100;

    private const double MaxCarMassKg =
        100_000;

    private const double MaxCarPowerHp =
        100_000;

    private const double MaxCarTorqueNm =
        100_000;

    private const double MaxSteeringLockPerSideDeg =
        180;

    private const double MaxCasterMagnitudeDeg =
        90;

    private const double MaxTireWidthMm =
        2000;

    private const double MaxEstimatedWheelTorqueNm =
        100_000;

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

    private static readonly CalibrationEngine CalibrationIdentity =
        new();

    public void Save(
        SavedTune tune,
        string path)
    {
        ArgumentNullException.ThrowIfNull(
            tune);

        var normalizedPath =
            NormalizePath(
                path);

        ValidateLoadedTune(
            tune);

        var bytes =
            SerializeTune(
                tune);

        // Validate the exact serialized representation too. This catches any
        // unexpected serialization/model interaction before a file is published.
        ValidateProfileBytes(
            bytes);

        var directory =
            Path.GetDirectoryName(
                normalizedPath);

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            throw new InvalidOperationException(
                "ADT could not determine the destination folder for the tune profile.");
        }

        lock (FileGate)
        {
            using var lease =
                AcquirePathMutex(
                    normalizedPath);

            Directory.CreateDirectory(
                directory);

            WriteAtomicallyAndVerify(
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
            using var lease =
                AcquirePathMutex(
                    normalizedPath);

            if (!File.Exists(
                    normalizedPath))
            {
                throw new FileNotFoundException(
                    "ADT tune profile was not found.",
                    normalizedPath);
            }

            if (Directory.Exists(
                    normalizedPath))
            {
                throw new InvalidDataException(
                    "ADT tune profile path points to a directory, not a file.");
            }

            byte[] bytes;

            try
            {
                bytes =
                    ReadAllBytesBounded(
                        normalizedPath);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
                when (
                    ex is IOException or
                    UnauthorizedAccessException or
                    NotSupportedException)
            {
                throw new IOException(
                    "ADT could not read the tune profile.",
                    ex);
            }

            return
                DeserializeAndValidate(
                    bytes);
        }
    }

    private static byte[] SerializeTune(
        SavedTune tune)
    {
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
                ex is JsonException or
                NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT could not serialize the tune profile.",
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
                "ADT tune profile contained invalid text data.",
                ex);
        }

        ValidateProfileSize(
            bytes.Length);

        return
            bytes;
    }

    private static SavedTune DeserializeAndValidate(
        byte[] bytes)
    {
        ValidateProfileBytes(
            bytes);

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

        return
            tune;
    }

    private static void ValidateProfileBytes(
        byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(
            bytes);

        ValidateProfileSize(
            bytes.Length);

        // System.Text.Json reads UTF-8 from bytes directly, but explicitly
        // decode once with a throwing decoder so malformed UTF-8 cannot be
        // silently normalized by a different consumer later.
        try
        {
            _ =
                StrictUtf8.GetString(
                    bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                "ADT tune profile is not valid UTF-8.",
                ex);
        }

        ValidateRequiredEnvelopeFields(
            bytes);
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

            RequireObject(
                root,
                "tune profile");

            var schema =
                RequireString(
                    root,
                    "Schema",
                    "tune profile");

            if (!string.Equals(
                    schema,
                    SavedTune.CurrentSchema,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"ADT tune profile schema '{schema}' is not supported by this build. Expected '{SavedTune.CurrentSchema}'.");
            }

            _ =
                RequireDateTime(
                    root,
                    "SavedUtc",
                    "tune profile");

            var input =
                RequireObjectProperty(
                    root,
                    "Input",
                    "tune profile");

            // TuneInput reference-property setters substitute defaults for null.
            // Require the actual context objects in the serialized file so a
            // truncated profile cannot silently turn into "default" hardware.
            _ =
                RequireObjectProperty(
                    input,
                    "Hardware",
                    "Input");

            _ =
                RequireObjectProperty(
                    input,
                    "Wheel",
                    "Input");

            _ =
                RequireObjectProperty(
                    input,
                    "DriftPack",
                    "Input");

            _ =
                RequireObjectProperty(
                    input,
                    "Car",
                    "Input");

            _ =
                RequireObjectProperty(
                    input,
                    "Intent",
                    "Input");

            _ =
                RequireObjectProperty(
                    root,
                    "Result",
                    "tune profile");

            // Calibration is optional by design. If present, it must be an object.
            if (
                TryGetPropertyIgnoreCase(
                    root,
                    "Calibration",
                    out var calibration) &&
                calibration.ValueKind is not
                    JsonValueKind.Null and not
                    JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "ADT tune profile field 'Calibration' must be an object or null.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "ADT tune profile contains invalid JSON.",
                ex);
        }
    }

    private static void ValidateLoadedTune(
        SavedTune tune)
    {
        if (tune.GetType() !=
            typeof(SavedTune))
        {
            throw new InvalidDataException(
                "ADT tune profile has an unexpected data type.");
        }

        if (!string.Equals(
                tune.Schema,
                SavedTune.CurrentSchema,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"ADT tune profile schema '{tune.Schema}' is not supported by this build. Expected '{SavedTune.CurrentSchema}'.");
        }

        ValidateSavedTimestamp(
            tune.SavedUtc);

        ValidateOptionalText(
            tune.Name,
            "profile name",
            MaxShortTextLength);

        ValidateInput(
            tune.Input);

        ValidateResult(
            tune.Result);

        if (tune.Calibration is not null)
        {
            ValidateCalibration(
                tune.Input,
                tune.Calibration);
        }
    }

    private static void ValidateSavedTimestamp(
        DateTime value)
    {
        if (!TryNormalizeUtc(
                value,
                out var utc))
        {
            throw new InvalidDataException(
                "ADT tune profile has an invalid SavedUtc timestamp.");
        }

        // Saved profiles intentionally do not expire. Only reject timestamps
        // that are implausibly in the future.
        if (utc >
            DateTime.UtcNow +
            MaximumFutureClockSkew)
        {
            throw new InvalidDataException(
                "ADT tune profile has a SavedUtc timestamp too far in the future.");
        }
    }

    private static void ValidateInput(
        TuneInput input)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        var hardware =
            input.Hardware
            ?? throw new InvalidDataException(
                "ADT tune profile is missing its wheelbase context.");

        var wheel =
            input.Wheel
            ?? throw new InvalidDataException(
                "ADT tune profile is missing its steering-wheel context.");

        var pack =
            input.DriftPack
            ?? throw new InvalidDataException(
                "ADT tune profile is missing its drift-pack context.");

        var car =
            input.Car
            ?? throw new InvalidDataException(
                "ADT tune profile is missing its car context.");

        var intent =
            input.Intent
            ?? throw new InvalidDataException(
                "ADT tune profile is missing its Drift Target context.");

        ValidateIdentity(
            hardware.Id,
            "wheelbase ID");

        ValidateOptionalText(
            hardware.Manufacturer,
            "wheelbase manufacturer",
            MaxShortTextLength);

        ValidateOptionalText(
            hardware.Model,
            "wheelbase model",
            MaxShortTextLength);

        ValidateFiniteRange(
            hardware.PeakTorqueNm,
            double.Epsilon,
            MaxPeakTorqueNm,
            "wheelbase peak torque");

        ValidateIntegerRange(
            hardware.MaxRotationDeg,
            1,
            MaxWheelbaseRotationDeg,
            "wheelbase maximum rotation");

        ValidateIdentity(
            wheel.Id,
            "steering-wheel ID");

        ValidateOptionalText(
            wheel.Manufacturer,
            "steering-wheel manufacturer",
            MaxShortTextLength);

        ValidateOptionalText(
            wheel.Model,
            "steering-wheel model",
            MaxShortTextLength);

        ValidateFiniteRange(
            wheel.DiameterMm,
            double.Epsilon,
            MaxWheelDiameterMm,
            "steering-wheel diameter");

        ValidateFiniteRange(
            wheel.InertiaFactor,
            double.Epsilon,
            MaxWheelInertiaFactor,
            "steering-wheel inertia factor");

        ValidateIdentity(
            pack.Id,
            "drift-pack ID");

        ValidateOptionalText(
            pack.Name,
            "drift-pack name",
            MaxShortTextLength);

        ValidateOptionalText(
            pack.Category,
            "drift-pack category",
            MaxShortTextLength);

        ValidateFiniteMagnitude(
            pack.GripBias,
            MaxProfileBiasMagnitude,
            "drift-pack grip bias");

        ValidateFiniteMagnitude(
            pack.SelfSteerBias,
            MaxProfileBiasMagnitude,
            "drift-pack self-steer bias");

        ValidateFiniteMagnitude(
            pack.DampingBias,
            MaxProfileBiasMagnitude,
            "drift-pack damping bias");

        ValidateFiniteMagnitude(
            pack.DetailBias,
            MaxProfileBiasMagnitude,
            "drift-pack detail bias");

        ValidateIdentity(
            car.Id,
            "car ID");

        ValidateIdentity(
            car.PackId,
            "car pack ID");

        if (!string.Equals(
                car.PackId,
                pack.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "ADT tune profile is internally inconsistent: the car's PackId does not match the saved DriftPack ID.");
        }

        ValidateOptionalText(
            car.DisplayName,
            "car display name",
            MaxShortTextLength);

        ValidateFiniteRange(
            car.MassKg,
            double.Epsilon,
            MaxCarMassKg,
            "car mass");

        ValidateFiniteRange(
            car.PowerHp,
            0,
            MaxCarPowerHp,
            "car power");

        ValidateFiniteRange(
            car.TorqueNm,
            0,
            MaxCarTorqueNm,
            "car torque");

        ValidateOptionalText(
            car.Drivetrain,
            "car drivetrain",
            MaxShortTextLength);

        ValidateFiniteRange(
            car.SteeringLockPerSideDeg,
            double.Epsilon,
            MaxSteeringLockPerSideDeg,
            "car steering lock");

        ValidateFiniteRange(
            car.CasterDeg,
            -MaxCasterMagnitudeDeg,
            MaxCasterMagnitudeDeg,
            "car caster");

        ValidateFiniteRange(
            car.FrontTireWidthMm,
            double.Epsilon,
            MaxTireWidthMm,
            "front tire width");

        ValidateFiniteRange(
            car.RearTireWidthMm,
            double.Epsilon,
            MaxTireWidthMm,
            "rear tire width");

        if (!Enum.IsDefined(
                car.Grip))
        {
            throw new InvalidDataException(
                $"ADT tune profile contains unsupported grip level '{car.Grip}'.");
        }

        ValidateConfidence(
            car.Confidence);

        ValidateOptionalText(
            car.SourceFolderName,
            "car source folder name",
            MaxShortTextLength);

        ValidateOptionalText(
            car.SourceFolderPath,
            "car source folder path",
            MaxPathTextLength,
            allowLineBreaks:
                false);

        ValidateOptionalText(
            car.Author,
            "car author",
            MaxShortTextLength);

        ValidateOptionalText(
            car.DataSourceSummary,
            "car data-source summary",
            MaxSummaryTextLength);

        if (car.IsInstalled)
        {
            ValidateInstalledCarFolderName(
                car.SourceFolderName);
        }

        if (!Enum.IsDefined(
                intent.Kind))
        {
            throw new InvalidDataException(
                $"ADT tune profile contains unsupported Drift Target kind '{intent.Kind}'.");
        }

        ValidateOptionalText(
            intent.Name,
            "Drift Target name",
            MaxShortTextLength);

        ValidateFiniteMagnitude(
            intent.SelfSteer,
            MaxProfileBiasMagnitude,
            "Drift Target self-steer value");

        ValidateFiniteMagnitude(
            intent.Stability,
            MaxProfileBiasMagnitude,
            "Drift Target stability value");

        ValidateFiniteMagnitude(
            intent.Detail,
            MaxProfileBiasMagnitude,
            "Drift Target detail value");

        ValidateFiniteMagnitude(
            intent.Weight,
            MaxProfileBiasMagnitude,
            "Drift Target weight value");
    }

    private static void ValidateConfidence(
        CarDataConfidence confidence)
    {
        ArgumentNullException.ThrowIfNull(
            confidence);

        ValidateEnum(
            confidence.Mass,
            "mass confidence");

        ValidateEnum(
            confidence.Power,
            "power confidence");

        ValidateEnum(
            confidence.Caster,
            "caster confidence");

        ValidateEnum(
            confidence.SteeringLock,
            "steering-lock confidence");

        ValidateEnum(
            confidence.FrontTireWidth,
            "front-tire confidence");

        ValidateEnum(
            confidence.Grip,
            "grip confidence");
    }

    private static void ValidateEnum<TEnum>(
        TEnum value,
        string description)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(
                value))
        {
            throw new InvalidDataException(
                $"ADT tune profile contains unsupported {description} value '{value}'.");
        }
    }

    private static void ValidateResult(
        TuneResult result)
    {
        ArgumentNullException.ThrowIfNull(
            result);

        ValidateFiniteRange(
            result.EstimatedPeakWheelTorqueNm,
            0,
            MaxEstimatedWheelTorqueNm,
            "estimated peak wheel torque");

        // These are presentation/assessment scores rather than a hardware write
        // boundary. Keep a broad corruption guard without inventing a tighter
        // semantic range that the model does not currently publish.
        ValidateIntegerRange(
            result.SelfSteerScore,
            -1_000_000,
            1_000_000,
            "self-steer score");

        ValidateIntegerRange(
            result.StabilityScore,
            -1_000_000,
            1_000_000,
            "stability score");

        ValidateIntegerRange(
            result.DetailScore,
            -1_000_000,
            1_000_000,
            "detail score");

        ValidateOptionalText(
            result.CalibrationSummary,
            "calibration summary",
            MaxSummaryTextLength,
            allowLineBreaks:
                true);

        if (result.Notes.Count >
            MaxNotes)
        {
            throw new InvalidDataException(
                $"ADT tune profile contains more than {MaxNotes} notes.");
        }

        foreach (var note in
                 result.Notes)
        {
            ValidateOptionalText(
                note,
                "tune note",
                MaxNoteLength,
                allowLineBreaks:
                    true);
        }

        ValidateAcResult(
            result.Ac);

        ValidateAzomResult(
            result.Azom);
    }

    private static void ValidateAcResult(
        AssettoCorsaSettings ac)
    {
        ArgumentNullException.ThrowIfNull(
            ac);

        ValidateIntegerRange(
            ac.GainPct,
            0,
            100,
            "Assetto Corsa gain");

        ValidateIntegerRange(
            ac.FilterPct,
            0,
            100,
            "Assetto Corsa filter");

        ValidateIntegerRange(
            ac.MinimumForcePct,
            0,
            100,
            "Assetto Corsa minimum force");

        ValidateIntegerRange(
            ac.KerbPct,
            0,
            100,
            "Assetto Corsa kerb effect");

        ValidateIntegerRange(
            ac.RoadPct,
            0,
            100,
            "Assetto Corsa road effect");

        ValidateIntegerRange(
            ac.SlipPct,
            0,
            100,
            "Assetto Corsa slip effect");

        ValidateIntegerRange(
            ac.AbsPct,
            0,
            100,
            "Assetto Corsa ABS effect");
    }

    private static void ValidateAzomResult(
        AzomSettings azom)
    {
        ArgumentNullException.ThrowIfNull(
            azom);

        var core =
            azom.Core;

        ValidateIntegerRange(
            core.WheelRotationAngleDeg,
            60,
            2700,
            "AZOM wheel rotation");

        ValidateIntegerRange(
            core.GameFfbStrengthPct,
            0,
            100,
            "AZOM game FFB strength");

        ValidateIntegerRange(
            core.BaseTorqueOutputPct,
            50,
            100,
            "AZOM base torque output");

        ValidateIntegerRange(
            core.MaximumWheelSpeedPct,
            0,
            200,
            "AZOM maximum wheel speed");

        ValidateIntegerRange(
            core.Interpolation,
            0,
            10,
            "AZOM interpolation");

        var shift =
            azom.GearshiftVibration;

        ValidateIntegerRange(
            shift.ShiftIntensity,
            0,
            5,
            "AZOM gearshift vibration");

        ValidateIntegerRange(
            shift.ShiftDebounceMs,
            0,
            1000,
            "AZOM shift debounce");

        var wheel =
            azom.WheelbaseEffects;

        ValidateIntegerRange(
            wheel.WheelDamperPct,
            0,
            100,
            "AZOM wheel damper");

        ValidateIntegerRange(
            wheel.WheelFrictionPct,
            0,
            100,
            "AZOM wheel friction");

        ValidateIntegerRange(
            wheel.NaturalInertia,
            100,
            500,
            "AZOM natural inertia");

        ValidateIntegerRange(
            wheel.WheelSpringPct,
            0,
            100,
            "AZOM wheel spring");

        var game =
            azom.GameEffects;

        ValidateIntegerRange(
            game.GameDamperPct,
            0,
            100,
            "AZOM game damper");

        ValidateIntegerRange(
            game.GameFrictionPct,
            0,
            100,
            "AZOM game friction");

        ValidateIntegerRange(
            game.GameInertiaPct,
            0,
            100,
            "AZOM game inertia");

        ValidateIntegerRange(
            game.GameSpringPct,
            0,
            100,
            "AZOM game spring");

        ValidateIntegerRange(
            azom.Protection.SteeringWheelInertia,
            100,
            4000,
            "AZOM steering-wheel inertia protection");

        ValidateIntegerRange(
            azom.SoftLimit.Stiffness,
            1,
            10,
            "AZOM soft-limit stiffness");

        var eq =
            azom.FfbEqualizer;

        ValidateIntegerRange(eq.Hz10, 0, 400, "AZOM EQ 10 Hz");
        ValidateIntegerRange(eq.Hz15, 0, 400, "AZOM EQ 15 Hz");
        ValidateIntegerRange(eq.Hz25, 0, 400, "AZOM EQ 25 Hz");
        ValidateIntegerRange(eq.Hz40, 0, 400, "AZOM EQ 40 Hz");
        ValidateIntegerRange(eq.Hz60, 0, 400, "AZOM EQ 60 Hz");
        ValidateIntegerRange(eq.Hz100, 0, 400, "AZOM EQ 100 Hz");
        ValidateIntegerRange(eq.Sensitivity, 0, 10, "AZOM road sensitivity");

        var curve =
            azom.FfbOutputCurve;

        if (!Enum.IsDefined(
                curve.Preset))
        {
            throw new InvalidDataException(
                $"ADT tune profile contains unsupported AZOM FFB curve preset '{curve.Preset}'.");
        }

        ValidateIntegerRange(curve.Node20, 0, 100, "AZOM FFB curve node 20");
        ValidateIntegerRange(curve.Node40, 0, 100, "AZOM FFB curve node 40");
        ValidateIntegerRange(curve.Node60, 0, 100, "AZOM FFB curve node 60");
        ValidateIntegerRange(curve.Node80, 0, 100, "AZOM FFB curve node 80");
        ValidateIntegerRange(curve.Node100, 0, 100, "AZOM FFB curve node 100");

        ValidateIntegerRange(
            azom.HighSpeedDamping.DampingLevelPct,
            0,
            100,
            "AZOM high-speed damping");

        ValidateIntegerRange(
            azom.HighSpeedDamping.TriggerSpeedKph,
            0,
            400,
            "AZOM high-speed damping trigger");

        ValidateOptionalText(
            azom.Miscellaneous.StandbyAfter,
            "AZOM standby timer text",
            MaxShortTextLength);
    }

    private static void ValidateCalibration(
        TuneInput input,
        CalibrationProfile calibration)
    {
        var expectedKey =
            CalibrationIdentity.BuildKey(
                input);

        if (!string.Equals(
                calibration.Key?.Trim(),
                expectedKey,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "ADT tune profile calibration belongs to a different wheelbase + wheel + pack + car identity.");
        }

        if (calibration.Samples <
            0)
        {
            throw new InvalidDataException(
                "ADT tune profile calibration has an invalid sample count.");
        }

        if (!TryNormalizeUtc(
                calibration.UpdatedUtc,
                out var updatedUtc))
        {
            throw new InvalidDataException(
                "ADT tune profile calibration has an invalid UpdatedUtc timestamp.");
        }

        if (updatedUtc >
            DateTime.UtcNow +
            MaximumFutureClockSkew)
        {
            throw new InvalidDataException(
                "ADT tune profile calibration timestamp is too far in the future.");
        }

        // These are the exact clamp bounds enforced by CalibrationEngine.
        ValidateIntegerRange(
            calibration.TorqueLimitDelta,
            -20,
            20,
            "calibration torque delta");

        ValidateIntegerRange(
            calibration.WheelSpeedDelta,
            -40,
            40,
            "calibration wheel-speed delta");

        ValidateIntegerRange(
            calibration.DampingDelta,
            -15,
            20,
            "calibration damping delta");

        ValidateIntegerRange(
            calibration.FrictionDelta,
            -10,
            12,
            "calibration friction delta");

        ValidateIntegerRange(
            calibration.SpeedDampingDelta,
            -10,
            20,
            "calibration high-speed damping delta");

        ValidateIntegerRange(
            calibration.InterpolationDelta,
            -3,
            4,
            "calibration interpolation delta");

        ValidateIntegerRange(
            calibration.AcGainDelta,
            -12,
            12,
            "calibration Assetto Corsa gain delta");
    }

    private static void ValidateInstalledCarFolderName(
        string? sourceFolderName)
    {
        if (string.IsNullOrWhiteSpace(
                sourceFolderName))
        {
            throw new InvalidDataException(
                "ADT tune profile marks the car as installed but does not include a source folder name.");
        }

        var name =
            sourceFolderName.Trim();

        if (
            name is "." or ".." ||
            Path.IsPathRooted(
                name) ||
            name.IndexOfAny(
                [
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar,
                    '\0',
                    '\r',
                    '\n'
                ]) >=
                0)
        {
            throw new InvalidDataException(
                "ADT tune profile contains an unsafe installed-car source folder name.");
        }
    }

    private static void ValidateIdentity(
        string? value,
        string description)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidDataException(
                $"ADT tune profile is missing its {description}.");
        }

        var normalized =
            value.Trim();

        if (
            normalized.Length >
                MaxIdLength ||
            normalized.Contains(
                '|') ||
            ContainsControlCharacters(
                normalized))
        {
            throw new InvalidDataException(
                $"ADT tune profile contains an invalid {description}.");
        }
    }

    private static void ValidateOptionalText(
        string? value,
        string description,
        int maximumLength,
        bool allowLineBreaks = false)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length >
            maximumLength)
        {
            throw new InvalidDataException(
                $"ADT tune profile {description} exceeds the supported {maximumLength:N0}-character limit.");
        }

        foreach (var character in
                 value)
        {
            if (
                character ==
                    '\0' ||
                (
                    char.IsControl(
                        character) &&
                    !(
                        allowLineBreaks &&
                        character is '\r' or '\n' or '\t'
                    )
                ))
            {
                throw new InvalidDataException(
                    $"ADT tune profile contains invalid control characters in {description}.");
            }
        }
    }

    private static bool ContainsControlCharacters(
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

    private static void ValidateFiniteRange(
        double value,
        double minimum,
        double maximum,
        string description)
    {
        if (
            !double.IsFinite(
                value) ||
            value <
                minimum ||
            value >
                maximum)
        {
            throw new InvalidDataException(
                $"ADT tune profile contains an invalid {description} value.");
        }
    }

    private static void ValidateFiniteMagnitude(
        double value,
        double maximumMagnitude,
        string description)
    {
        if (
            !double.IsFinite(
                value) ||
            Math.Abs(
                value) >
                maximumMagnitude)
        {
            throw new InvalidDataException(
                $"ADT tune profile contains an invalid {description} value.");
        }
    }

    private static void ValidateIntegerRange(
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
                $"ADT tune profile contains {description}={value}, outside the supported {minimum}..{maximum} range.");
        }
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
                            "ADT tune timestamps must include UTC/local time semantics."),

                    _ =>
                        throw new InvalidDataException(
                            "ADT tune timestamp kind is invalid.")
                };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReadAllBytesBounded(
        string path)
    {
        var info =
            new FileInfo(
                path);

        if (
            info.Length <=
                0 ||
            info.Length >
                MaxProfileFileBytes)
        {
            throw new InvalidDataException(
                "ADT tune profile has an invalid file size.");
        }

        using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                32 * 1024,
                FileOptions.SequentialScan);

        ValidateProfileSize(
            stream.Length);

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
                    "ADT tune profile ended unexpectedly while being read.");
            }

            offset +=
                read;
        }

        return
            bytes;
    }

    private static void ValidateProfileSize(
        long length)
    {
        if (
            length <=
                0 ||
            length >
                MaxProfileFileBytes)
        {
            throw new InvalidDataException(
                $"ADT tune profile size must be between 1 and {MaxProfileFileBytes:N0} bytes.");
        }
    }

    private static void WriteAtomicallyAndVerify(
        string destinationPath,
        string directory,
        byte[] bytes)
    {
        var fileName =
            Path.GetFileName(
                destinationPath);

        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            throw new InvalidDataException(
                "ADT tune profile destination does not contain a valid file name.");
        }

        if (Directory.Exists(
                destinationPath))
        {
            throw new IOException(
                "ADT cannot save a tune profile over a directory.");
        }

        var temporaryPath =
            Path.Combine(
                directory,
                $"{fileName}.{Guid.NewGuid():N}.tmp");

        byte[]? previousBytes =
            null;

        var previousExisted =
            File.Exists(
                destinationPath);

        if (previousExisted)
        {
            try
            {
                var existingInfo =
                    new FileInfo(
                        destinationPath);

                if (
                    existingInfo.Length >
                        0 &&
                    existingInfo.Length <=
                        MaxProfileFileBytes)
                {
                    previousBytes =
                        ReadAllBytesBounded(
                            destinationPath);
                }
            }
            catch
            {
                // The user explicitly selected this destination and Windows'
                // Save dialog handles overwrite intent. Failure to snapshot the
                // old arbitrary file does not make the new profile invalid.
                previousBytes =
                    null;
            }
        }

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
                    flushToDisk:
                        true);
            }

            // A complete valid SavedTune must exist before publication.
            var stagedBytes =
                ReadAllBytesBounded(
                    temporaryPath);

            _ =
                DeserializeAndValidate(
                    stagedBytes);

            var expectedHash =
                SHA256.HashData(
                    bytes);

            var stagedHash =
                SHA256.HashData(
                    stagedBytes);

            if (!CryptographicOperations.FixedTimeEquals(
                    expectedHash,
                    stagedHash))
            {
                throw new IOException(
                    "ADT tune profile staging verification failed.");
            }

            File.Move(
                temporaryPath,
                destinationPath,
                overwrite:
                    true);

            try
            {
                var published =
                    ReadAllBytesBounded(
                        destinationPath);

                _ =
                    DeserializeAndValidate(
                        published);

                var publishedHash =
                    SHA256.HashData(
                        published);

                if (!CryptographicOperations.FixedTimeEquals(
                        expectedHash,
                        publishedHash))
                {
                    throw new IOException(
                        "ADT tune profile verification failed after publishing the file.");
                }
            }
            catch
            {
                RestorePreviousDestinationBestEffort(
                    destinationPath,
                    directory,
                    fileName,
                    previousExisted,
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

    private static void RestorePreviousDestinationBestEffort(
        string destinationPath,
        string directory,
        string fileName,
        bool previousExisted,
        byte[]? previousBytes)
    {
        try
        {
            if (
                previousExisted &&
                previousBytes is
                    { Length: > 0 })
            {
                var restorePath =
                    Path.Combine(
                        directory,
                        $"{fileName}.{Guid.NewGuid():N}.restore.tmp");

                try
                {
                    using (
                        var stream =
                            new FileStream(
                                restorePath,
                                FileMode.CreateNew,
                                FileAccess.Write,
                                FileShare.None,
                                32 * 1024,
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

                    File.Move(
                        restorePath,
                        destinationPath,
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

            if (!previousExisted)
            {
                TryDeleteFile(
                    destinationPath);
            }
        }
        catch
        {
            // Best-effort recovery only. Never hide the primary save failure.
        }
    }

    private static string NormalizePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            throw new ArgumentException(
                "Tune profile path is required.",
                nameof(path));
        }

        try
        {
            var expanded =
                Environment.ExpandEnvironmentVariables(
                    path
                        .Trim()
                        .Trim('"'));

            if (string.IsNullOrWhiteSpace(
                    expanded))
            {
                throw new ArgumentException(
                    "Tune profile path is required.",
                    nameof(path));
            }

            var fullPath =
                Path.GetFullPath(
                    expanded);

            if (fullPath.Length >
                MaxPathTextLength)
            {
                throw new ArgumentException(
                    "Tune profile path is too long.",
                    nameof(path));
            }

            return
                fullPath;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
            when (
                ex is NotSupportedException or
                PathTooLongException)
        {
            throw new ArgumentException(
                "Tune profile path is invalid.",
                nameof(path),
                ex);
        }
    }

    private static PathMutexLease AcquirePathMutex(
        string normalizedPath)
    {
        var canonical =
            OperatingSystem.IsWindows()
                ? normalizedPath.ToUpperInvariant()
                : normalizedPath;

        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    canonical));

        // Never place the user's path itself in a named kernel object.
        var name =
            @"Local\AtomicDriftTuner.Profile." +
            Convert.ToHexString(
                hash.AsSpan(
                    0,
                    16));

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
                "ADT could not get exclusive access to this tune profile because another ADT process is using the same file.");
        }

        return
            new PathMutexLease(
                mutex);
    }

    private static JsonElement RequireObjectProperty(
        JsonElement parent,
        string name,
        string ownerDescription)
    {
        if (
            !TryGetPropertyIgnoreCase(
                parent,
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"ADT {ownerDescription} is missing required object '{name}'.");
        }

        return
            value;
    }

    private static string RequireString(
        JsonElement parent,
        string name,
        string ownerDescription)
    {
        if (
            !TryGetPropertyIgnoreCase(
                parent,
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"ADT {ownerDescription} is missing required text field '{name}'.");
        }

        var text =
            value.GetString();

        if (string.IsNullOrWhiteSpace(
                text))
        {
            throw new InvalidDataException(
                $"ADT {ownerDescription} field '{name}' is blank.");
        }

        return
            text.Trim();
    }

    private static DateTime RequireDateTime(
        JsonElement parent,
        string name,
        string ownerDescription)
    {
        if (
            !TryGetPropertyIgnoreCase(
                parent,
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.String ||
            !value.TryGetDateTime(
                out var timestamp))
        {
            throw new InvalidDataException(
                $"ADT {ownerDescription} is missing valid timestamp '{name}'.");
        }

        return
            timestamp;
    }

    private static void RequireObject(
        JsonElement element,
        string description)
    {
        if (element.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"ADT {description} root must be a JSON object.");
        }
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
            // Cleanup failure must never hide the actual operation result.
        }
    }

    private sealed class PathMutexLease : IDisposable
    {
        private Mutex? _mutex;

        public PathMutexLease(
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
                // Defensive only: the lease should always own the mutex here.
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }
}
