using System.Globalization;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Shared export/staging contract; neither consumer may bypass generation's constraints.</summary>
public static class SetupChangeValidation
{
    public static string? Restriction(CarSetupAnalysis analysis, CarSetupParameter parameter)
    {
        var physics = analysis.Physics;
        if (physics is not null && (physics.Available || physics.HasSetupDefinition) &&
            !physics.CarId.Equals(analysis.CarFolderName, StringComparison.OrdinalIgnoreCase))
            return "Imported car definitions belong to a different car. Reload the selected baseline.";
        if (parameter.Range?.UnavailableReason is { } unavailable) return unavailable;
        if (parameter.Section.StartsWith("DIFF_", StringComparison.OrdinalIgnoreCase) && physics?.DriveType is "FWD" or "AWD" or "AWD2")
            return $"Imported drivetrain is {physics.DriveType}; ADT's current rear-drive differential advice is not verified for this drivetrain. Tune this control manually.";
        var definitionSection = parameter.Section.Equals("FINAL_RATIO", StringComparison.OrdinalIgnoreCase) ? "FINAL_GEAR_RATIO" : parameter.Section;
        if (physics?.HasSetupDefinition == true && !physics.AdjustableSections.Contains(definitionSection, StringComparer.OrdinalIgnoreCase))
            return "This saved control is not exposed in the imported setup.ini. ADT will not invent an adjustable control from a base physics value.";
        return null;
    }

    public static void Validate(CarSetupAnalysis analysis, CarSetupParameter parameter, double before, double after, bool allowFinalDriveExport = false)
    {
        if (Restriction(analysis, parameter) is { } reason) throw new InvalidDataException(reason);
        if (allowFinalDriveExport && parameter.Section.Equals("FINAL_RATIO", StringComparison.OrdinalIgnoreCase))
        {
            ValidateSerialized(parameter, before, after);
            // Gearing uses a ratio-list contract, never the ordinary scalar rules.
            // Re-decode both selections against the same immutable source before writing.
            if (analysis.SourceEvidence is not { } evidence) throw new InvalidDataException("Reload the final-drive definition before saving.");
            CarDataSource.EnsureUnchanged(evidence);
            var source = CarDataSource.Open(new CarProfile { SourceFolderName = analysis.CarFolderName, SourceFolderPath = evidence.CarPath });
            if (source.Evidence.Fingerprint != evidence.Fingerprint) throw new InvalidDataException("Final-drive data changed; calculate again.");
            var decoder = new CarSetupDecoder();
            foreach (var value in new[] { parameter.CurrentRaw, parameter.RecommendedRaw })
                if (decoder.Decode([new() { Section = parameter.Section, CurrentRaw = value }], source.ReadText).Single().Status != DecodedSetupSetting.Verified)
                    throw new InvalidDataException("The final-drive selection has no verified ratio-list entry. Calculate again.");
            CarDataSource.EnsureUnchanged(evidence);
            return;
        }
        if (!SetupValueMapping.TryCreate(parameter.Section, parameter.Range, out var mapping))
            throw new InvalidDataException($"{parameter.Section} has no verified saved-value mapping with a numeric minimum, maximum and step. Reload a supported car definition.");
        if (!mapping.IsLegal(before) || !mapping.IsLegal(after))
            throw new InvalidDataException($"{parameter.Section} is outside its supported range or does not match a legal setup step. Reload a valid baseline.");
        ValidateSerialized(parameter, before, after);
    }

    private static void ValidateSerialized(CarSetupParameter parameter, double before, double after)
    {
        if (!double.TryParse(parameter.CurrentRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawBefore) ||
            !PitSetupPlanService.NumbersEqual(rawBefore, before) ||
            !double.TryParse(parameter.RecommendedRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawAfter) ||
            !PitSetupPlanService.NumbersEqual(rawAfter, after))
            throw new InvalidDataException($"{parameter.Section} cannot be represented reliably in a saved setup VALUE.");
    }
}
