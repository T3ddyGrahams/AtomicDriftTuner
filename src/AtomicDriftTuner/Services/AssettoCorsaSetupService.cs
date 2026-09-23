using System.Globalization;
using System.Text;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AssettoCorsaSetupService
{
    private const long MaximumBaselineBytes =
        4 * 1024 * 1024;

    private const long MaximumDefinitionBytes =
        4 * 1024 * 1024;

    private const int MaximumSetupFilesReturned =
        2000;

    private readonly AppSettingsStore _settingsStore =
        new();

    public string GetDefaultSetupsRoot()
    {
        var configured =
            _settingsStore
                .Load()
                .AssettoCorsaDocumentsRoot;

        string userRoot;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            userRoot =
                NormalizeDirectoryPath(
                    configured,
                    "configured Assetto Corsa documents root");
        }
        else
        {
            var documents =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);

            if (string.IsNullOrWhiteSpace(documents))
            {
                throw new DirectoryNotFoundException(
                    "ADT could not determine the current user's Documents folder.");
            }

            userRoot =
                Path.GetFullPath(
                    Path.Combine(
                        documents,
                        "Assetto Corsa"));
        }

        return Path.Combine(
            userRoot,
            "setups");
    }

    public List<string> FindSavedSetups(
        CarProfile car,
        string? setupsRoot = null)
    {
        ArgumentNullException.ThrowIfNull(
            car);

        if (string.IsNullOrWhiteSpace(
                car.SourceFolderName))
        {
            return [];
        }

        var root =
            string.IsNullOrWhiteSpace(setupsRoot)
                ? GetDefaultSetupsRoot()
                : NormalizeDirectoryPath(
                    setupsRoot,
                    "Assetto Corsa setups root");

        if (!Directory.Exists(root))
        {
            return [];
        }

        var carFolderName =
            ValidateSinglePathSegment(
                car.SourceFolderName,
                "car folder name");

        var carDirectory =
            Path.GetFullPath(
                Path.Combine(
                    root,
                    carFolderName));

        EnsurePathInsideRoot(
            root,
            carDirectory,
            "car setup folder");

        if (!Directory.Exists(
                carDirectory))
        {
            return [];
        }

        var options =
            new EnumerationOptions
            {
                RecurseSubdirectories =
                    true,

                IgnoreInaccessible =
                    true,

                AttributesToSkip =
                    FileAttributes.ReparsePoint
            };

        return Directory
            .EnumerateFiles(
                carDirectory,
                "*.ini",
                options)
            .Select(
                path =>
                    new
                    {
                        Path =
                            path,

                        LastWriteUtc =
                            SafeLastWriteTimeUtc(
                                path)
                    })
            .OrderByDescending(
                item =>
                    item.LastWriteUtc)
            .Take(
                MaximumSetupFilesReturned)
            .Select(
                item =>
                    item.Path)
            .ToList();
    }

    public CarSetupAnalysis LoadBaseline(
        string path,
        CarProfile car,
        bool importPhysics = true)
    {
        ArgumentNullException.ThrowIfNull(
            car);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Baseline setup path is required.",
                nameof(path));
        }

        var fullPath =
            NormalizeFilePath(
                path,
                "baseline setup");

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Baseline setup not found.",
                fullPath);
        }

        EnsureFileSize(
            fullPath,
            MaximumBaselineBytes,
            "Baseline setup");

        var definitions =
            LoadDefinitions(
                car, out var source);
        var hasUnassigned = false;
        var decodeWarnings = new List<string>();
        var modelNames = new List<string>();

        var parameters =
            new List<CarSetupParameter>();

        var section =
            string.Empty;

        foreach (var rawLine in
                 ReadAllLinesBounded(
                     fullPath,
                     MaximumBaselineBytes))
        {
            var line =
                rawLine.Trim();

            if (TryReadSectionHeader(
                    line,
                    out var parsedSection))
            {
                section =
                    parsedSection;

                continue;
            }

            if (line.StartsWith('[')) section = string.Empty; // A malformed header must not inherit the prior control.
            var modelEquals = line.IndexOf('=');
            if (section.Equals("CAR", StringComparison.OrdinalIgnoreCase) && modelEquals > 0 &&
                line[..modelEquals].Trim().Equals("MODEL", StringComparison.OrdinalIgnoreCase))
                modelNames.Add(line[(modelEquals + 1)..].Split(';')[0].Trim());

            if (
                string.IsNullOrWhiteSpace(section) ||
                !line.StartsWith(
                    "VALUE=",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(section) && line.StartsWith("VALUE=", StringComparison.OrdinalIgnoreCase))
                    hasUnassigned = true;
                continue;
            }

            var equalsIndex =
                line.IndexOf('=');

            if (equalsIndex < 0)
            {
                continue;
            }

            var raw =
                line[(equalsIndex + 1)..]
                    .Trim();

            double? numeric =
                TryNum(
                    raw,
                    out var value)
                    ? value
                    : null;

            definitions.TryGetValue(
                section,
                out var range);
            if (definitions.TryGetValue("*", out var unreadable))
                range = new() { Section = section, UnavailableReason = unreadable.UnavailableReason };

            parameters.Add(
                new CarSetupParameter
                {
                    Section =
                        section,

                    Category =
                        Classify(
                            section),

                    CurrentRaw =
                        raw,

                    CurrentValue =
                        numeric,

                    RecommendedValue =
                        numeric,

                    Range =
                        range
                });
        }

        if (parameters.Count == 0)
        {
            throw new InvalidDataException(
                "This file does not contain Assetto Corsa setup sections with VALUE= entries.");
        }

        var selectedId = car.SourceFolderName?.Trim();
        if (string.IsNullOrWhiteSpace(selectedId) && !string.IsNullOrWhiteSpace(car.SourceFolderPath))
            selectedId = Path.GetFileName(Path.TrimEndingDirectorySeparator(car.SourceFolderPath));
        if (modelNames.Count > 1 || modelNames.Count == 1 && !string.IsNullOrWhiteSpace(selectedId) &&
            !modelNames[0].Equals(selectedId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The baseline's CAR/MODEL is duplicated or belongs to a different car. Choose a setup saved for the selected car.");
        var identityKnown = modelNames.Count == 1 && !string.IsNullOrWhiteSpace(selectedId) && modelNames[0].Equals(selectedId, StringComparison.OrdinalIgnoreCase);
        if (!identityKnown) decodeWarnings.Add("The baseline has no verified CAR/MODEL identity. Its values are preserved, but decoded selections cannot be verified against this car. Save a baseline from the selected car in game.");
        if (hasUnassigned) decodeWarnings.Add("This saved setup contains VALUE entries without a section name. ADT preserves them but cannot identify their control or assume they are an ECU map. Confirm these settings in game; a complete named setup capture is needed for attribution.");
        decodeWarnings.AddRange(definitions.Values.Where(d => d.UnavailableReason is not null)
            .Select(d => d.UnavailableReason!).Distinct(StringComparer.Ordinal));
        var physics = new CarPhysicsService().Read(car, parameters, importPhysics, source);
        if (!identityKnown) physics = physics with { DecodedSettings = Array.AsReadOnly(physics.DecodedSettings.Select(d => d.Status == DecodedSetupSetting.Verified ?
            d with { Status = DecodedSetupSetting.Partial, Explanation = "Baseline car identity is unverified; this is only a candidate interpretation for the selected car. " + d.Explanation } : d).ToArray()) };
        foreach (var parameter in parameters)
            parameter.DecodedContext = physics.DecodedSettings.FirstOrDefault(d => d.Section.Equals(parameter.Section, StringComparison.OrdinalIgnoreCase))?.Display ?? "Not decoded.";
        return new CarSetupAnalysis
        {
            Physics = physics, SourceEvidence = source?.Evidence, DecodeWarnings = decodeWarnings, HasUnassignedValues = hasUnassigned, BaselineIdentityVerified = identityKnown,
            BaselinePath =
                fullPath,

            CarFolderName =
                string.IsNullOrWhiteSpace(
                    car.SourceFolderName)
                    ? "unknown-car"
                    : car.SourceFolderName.Trim(),

            SetupDefinitionPath =
                definitions.Count > 0 ? source?.Kind == "packed" ? "data.acd → setup.ini" : FindSetupDefinition(car) : null,

            Parameters =
                parameters
        };
    }

    public string WriteGenerated(
        CarSetupAnalysis analysis,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(
            analysis);

        CarPhysicsService.EnsureUnchanged(analysis.Physics);
        if (analysis.SourceEvidence is not null) CarDataSource.EnsureUnchanged(analysis.SourceEvidence);

        if (string.IsNullOrWhiteSpace(
                analysis.BaselinePath))
        {
            throw new InvalidDataException(
                "ADT cannot generate an AC setup because the baseline path is missing.");
        }

        if (analysis.Parameters is null)
        {
            throw new InvalidDataException(
                "ADT cannot generate an AC setup because the analyzed parameter list is missing.");
        }

        if (string.IsNullOrWhiteSpace(
                outputPath))
        {
            throw new ArgumentException(
                "Output path is required.",
                nameof(outputPath));
        }

        var sourceFull =
            NormalizeFilePath(
                analysis.BaselinePath,
                "baseline setup");

        var outputFull =
            NormalizeFilePath(
                outputPath,
                "generated setup");

        if (!File.Exists(sourceFull))
        {
            throw new FileNotFoundException(
                "Baseline setup not found.",
                sourceFull);
        }

        EnsureFileSize(
            sourceFull,
            MaximumBaselineBytes,
            "Baseline setup");

        if (sourceFull.Equals(
                outputFull,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Choose a new filename. ADT will not overwrite the baseline setup.");
        }

        var outputDirectory =
            Path.GetDirectoryName(
                outputFull)
            ?? throw new InvalidOperationException(
                "Invalid output folder.");

        Directory.CreateDirectory(
            outputDirectory);

        var replacements =
            BuildReplacementMap(
                analysis.Parameters);

        var expected = analysis.Parameters
            .Where(p => p is not null && p.Changed && replacements.ContainsKey(p.Section.Trim()))
            .ToDictionary(p => p.Section.Trim(), p => p.CurrentRaw.Trim(), StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var outputLines =
            new List<string>();

        var section =
            string.Empty;

        foreach (var rawLine in
                 ReadAllLinesBounded(
                     sourceFull,
                     MaximumBaselineBytes))
        {
            var trimmed =
                rawLine.Trim();

            if (TryReadSectionHeader(
                    trimmed,
                    out var parsedSection))
            {
                section =
                    parsedSection;
            }

            if (
                trimmed.StartsWith(
                    "VALUE=",
                    StringComparison.OrdinalIgnoreCase) &&
                replacements.TryGetValue(
                    section,
                    out var replacement))
            {
                var currentRaw = trimmed[(trimmed.IndexOf('=') + 1)..].Trim();
                if (!matched.Add(section) || !string.Equals(currentRaw, expected[section], StringComparison.Ordinal))
                    throw new InvalidDataException("The baseline setup changed after analysis. Reload it and generate recommendations again.");
                outputLines.Add($"VALUE={replacement}");
            }
            else
            {
                outputLines.Add(
                    rawLine);
            }
        }

        if (matched.Count != replacements.Count)
            throw new InvalidDataException("The baseline setup changed after analysis. Reload it and generate recommendations again.");

        WriteAllLinesAtomic(
            outputFull,
            outputLines);

        return outputFull;
    }

    private Dictionary<string, SetupRangeDefinition> LoadDefinitions(
        CarProfile car, out CarDataSource? source)
    {
        source = null;
        var result =
            new Dictionary<
                string,
                SetupRangeDefinition>(
                StringComparer.OrdinalIgnoreCase);

        string? text;
        try { source = CarDataSource.Open(car); text = source.ReadText("setup.ini"); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { return result; }
        if (text is null) return result;

        CarSetupDefinitionFile parsed;
        try { parsed = CarSetupDefinitionFile.Parse(text); }
        catch (InvalidDataException ex)
        {
            // Mark every identifiable control unavailable when section boundaries are unsafe.
            // A wildcard is consumed by LoadBaseline, never treated as a setup parameter.
            result["*"] = new() { Section = "*", UnavailableReason = ex.Message };
            return result;
        }
        var raw = parsed.Sections;
        foreach (var item in parsed.InvalidSections)
            result[item.Key] = new() { Section = item.Key, UnavailableReason = CarSetupDefinitionFile.Warning(item.Key, item.Value) };

        var globalClicks =
            raw.TryGetValue(
                "DISPLAY_METHOD",
                out var display) &&
            display.TryGetValue(
                "SHOW_CLICKS",
                out var clicks) &&
            IsTruthyOne(
                clicks);

        foreach (var pair in raw)
        {
            var name =
                pair.Key;

            if (parsed.InvalidSections.ContainsKey(name)) continue;

            var values =
                pair.Value;

            if (!values.ContainsKey("SHOW_CLICKS") && parsed.InvalidSections.ContainsKey("DISPLAY_METHOD"))
            {
                result[name] = new() { Section = name, UnavailableReason = $"setup.ini [{name}] inherits an ambiguous DISPLAY_METHOD; this control is left unchanged." };
                continue;
            }

            var sectionClicks =
                globalClicks;

            if (values.TryGetValue(
                    "SHOW_CLICKS",
                    out var sectionClickValue))
            {
                sectionClicks =
                    IsTruthyOne(
                        sectionClickValue);
            }

            var definition =
                new SetupRangeDefinition
                {
                    Section =
                        name,

                    ShowClicks =
                        sectionClicks,

                    Source =
                        source.Kind == "packed" ? "data.acd → setup.ini" : "data/setup.ini"
                };

            if (
                values.TryGetValue(
                    "MIN",
                    out var min) &&
                TryNum(
                    min,
                    out var minimum))
            {
                definition.Min =
                    minimum;
            }

            if (
                values.TryGetValue(
                    "MAX",
                    out var max) &&
                TryNum(
                    max,
                    out var maximum))
            {
                definition.Max =
                    maximum;
            }

            if (
                values.TryGetValue(
                    "STEP",
                    out var step) &&
                TryNum(
                    step,
                    out var stepValue) &&
                stepValue >
                0)
            {
                definition.Step =
                    stepValue;
            }

            if (
                definition.Min is double minValue &&
                definition.Max is double maxValue &&
                minValue >
                maxValue)
            {
                // Invalid range metadata is safer to ignore than to force onto
                // saved setup values.
                definition.Min =
                    null;

                definition.Max =
                    null;

                definition.Step =
                    null;
            }

            if (values.TryGetValue(
                    "NAME",
                    out var friendly))
            {
                definition.Name =
                    NormalizeMetadataText(
                        friendly);
            }

            if (values.TryGetValue(
                    "UNITS",
                    out var units))
            {
                definition.Units =
                    NormalizeMetadataText(
                        units);
            }

            if (
                definition.Min is not null ||
                definition.Max is not null ||
                definition.Step is not null ||
                definition.Name is not null ||
                definition.Units is not null)
            {
                result[name] =
                    definition;
            }
            if (CamberSetupValues.IsCamber(name) && !values.ContainsKey("LUT") && !values.ContainsKey("RATIOS"))
            {
                // Local mode is explicit. Global 0/1 have the same camber serialization;
                // global-only offset/unknown modes are not inferred from display metadata.
                var localMode = values.GetValueOrDefault("SHOW_CLICKS");
                var globalMode = raw.GetValueOrDefault("DISPLAY_METHOD")?.GetValueOrDefault("SHOW_CLICKS");
                var modeText = localMode ?? globalMode ?? "0";
                if (int.TryParse(modeText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var mode) &&
                    mode is >= 0 and <= 2 && (localMode is not null || mode != 2)) definition.CamberValueMode = mode;
            }
        }

        return result;
    }

    private static Dictionary<string, string> BuildReplacementMap(
        IEnumerable<CarSetupParameter> parameters)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameters)
        {
            if (
                parameter is null ||
                !parameter.Changed ||
                string.IsNullOrWhiteSpace(
                    parameter.Section))
            {
                continue;
            }

            var section =
                parameter.Section.Trim();

            if (section.Length == 0)
            {
                continue;
            }

            var replacement =
                parameter.RecommendedRaw;

            if (parameter.Range?.UnavailableReason is { } unavailable)
                throw new InvalidDataException(unavailable);

            if (CamberSetupValues.IsCamber(section) &&
                (parameter.Range is null || !parameter.Range.Section.Equals(section, StringComparison.OrdinalIgnoreCase) ||
                 parameter.CurrentValue is not double before || parameter.RecommendedValue is not double after ||
                 !CamberSetupValues.IsLegal(parameter.Range, before) || !CamberSetupValues.IsLegal(parameter.Range, after) ||
                 !TryNum(replacement, out var serialized) || !PitSetupPlanService.NumbersEqual(after, serialized)))
                throw new InvalidDataException($"{section} cannot be saved: its camber VALUE is outside a verified range or step. Reload the baseline and generate again.");

            if (string.IsNullOrWhiteSpace(
                    replacement))
            {
                continue;
            }

            // If a malformed baseline somehow produced duplicate section
            // objects, use the last analyzed recommendation rather than
            // throwing during dictionary construction.
            result[section] =
                replacement.Trim();
        }

        return result;
    }

    private static string? FindSetupDefinition(
        CarProfile car)
    {
        ArgumentNullException.ThrowIfNull(
            car);

        if (string.IsNullOrWhiteSpace(
                car.SourceFolderPath))
        {
            return null;
        }

        string carRoot;

        try
        {
            carRoot =
                Path.GetFullPath(
                    car.SourceFolderPath.Trim());
        }
        catch (
            Exception ex)
            when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
        {
            return null;
        }

        var candidate =
            Path.GetFullPath(
                Path.Combine(
                    carRoot,
                    "data",
                    "setup.ini"));

        try
        {
            EnsurePathInsideRoot(
                carRoot,
                candidate,
                "setup definition");
        }
        catch
        {
            return null;
        }

        return File.Exists(candidate)
            ? candidate
            : null;
    }

    private static List<string> ReadAllLinesBounded(
        string path,
        long maximumBytes)
    {
        EnsureFileSize(
            path,
            maximumBytes,
            "Text file");

        var result =
            new List<string>();

        using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

        using var reader =
            new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

        while (reader.ReadLine() is string line)
        {
            result.Add(
                line);

            if (stream.Position > maximumBytes)
            {
                throw new InvalidDataException(
                    $"ADT refused to read '{path}' because it exceeded the {maximumBytes:N0}-byte safety limit.");
            }
        }

        return result;
    }

    private static void WriteAllLinesAtomic(
        string destination,
        IEnumerable<string> lines)
    {
        var directory =
            Path.GetDirectoryName(
                destination)
            ?? throw new InvalidOperationException(
                "Invalid output folder.");

        Directory.CreateDirectory(
            directory);

        var temporary =
            Path.Combine(
                directory,
                $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (
                var stream =
                    new FileStream(
                        temporary,
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
                foreach (var line in lines)
                {
                    writer.WriteLine(
                        line);
                }

                writer.Flush();

                stream.Flush(
                    flushToDisk: true);
            }

            File.Move(
                temporary,
                destination,
                overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(
                        temporary))
                {
                    File.Delete(
                        temporary);
                }
            }
            catch
            {
                // Cleanup failure must not hide the original write failure.
            }
        }
    }

    private static void EnsureFileSize(
        string path,
        long maximumBytes,
        string description)
    {
        var info =
            new FileInfo(
                path);

        if (!info.Exists)
        {
            return;
        }

        if (info.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"{description} is unexpectedly large ({info.Length:N0} bytes). ADT will not process files larger than {maximumBytes:N0} bytes.");
        }
    }

    private static string NormalizeDirectoryPath(
        string path,
        string description)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            throw new ArgumentException(
                $"{description} is required.");
        }

        try
        {
            return Path.GetFullPath(
                path.Trim());
        }
        catch (
            Exception ex)
            when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
        {
            throw new InvalidDataException(
                $"ADT could not use the {description} path.",
                ex);
        }
    }

    private static string NormalizeFilePath(
        string path,
        string description)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            throw new ArgumentException(
                $"{description} path is required.");
        }

        try
        {
            return Path.GetFullPath(
                path.Trim());
        }
        catch (
            Exception ex)
            when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
        {
            throw new InvalidDataException(
                $"ADT could not use the {description} path.",
                ex);
        }
    }

    private static string ValidateSinglePathSegment(
        string value,
        string description)
    {
        var trimmed =
            value.Trim();

        if (
            trimmed.Length == 0 ||
            trimmed is "." or ".." ||
            trimmed.IndexOfAny(
                Path.GetInvalidFileNameChars()) >=
            0 ||
            trimmed.Contains(
                Path.DirectorySeparatorChar) ||
            trimmed.Contains(
                Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException(
                $"ADT cannot use the {description} because it is not a valid single folder name.");
        }

        return trimmed;
    }

    private static void EnsurePathInsideRoot(
        string root,
        string candidate,
        string description)
    {
        var rootFull =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    root));

        var candidateFull =
            Path.GetFullPath(
                candidate);

        var rootPrefix =
            rootFull +
            Path.DirectorySeparatorChar;

        if (
            !candidateFull.Equals(
                rootFull,
                StringComparison.OrdinalIgnoreCase) &&
            !candidateFull.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"ADT refused to use the {description} because it resolves outside the expected Assetto Corsa folder.");
        }
    }

    private static bool TryReadSectionHeader(
        string line,
        out string section)
    {
        section =
            string.Empty;

        if (
            line.Length < 3 ||
            !line.StartsWith(
                '[') ||
            !line.EndsWith(
                ']'))
        {
            return false;
        }

        var value =
            line[1..^1]
                .Trim();

        if (value.Length == 0)
        {
            return false;
        }

        section =
            value;

        return true;
    }

    private static bool TryNum(
        string raw,
        out double value)
    {
        if (
            double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) &&
            double.IsFinite(
                value))
        {
            return true;
        }

        value =
            0;

        return false;
    }

    private static bool IsTruthyOne(
        string value)
    {
        return string.Equals(
            value.Trim(),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeMetadataText(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        var cleaned =
            new string(
                value
                    .Trim()
                    .Where(
                        character =>
                            !char.IsControl(
                                character))
                    .ToArray());

        return cleaned.Length == 0
            ? null
            : cleaned;
    }

    private static DateTime SafeLastWriteTimeUtc(
        string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(
                path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static CarSetupCategory Classify(
        string section)
    {
        var value =
            section
                .Trim()
                .ToUpperInvariant();

        if (
            value.StartsWith(
                "PRESSURE") ||
            value ==
            "TYRES")
        {
            return CarSetupCategory.Tires;
        }

        if (
            value.StartsWith(
                "CAMBER") ||
            value.StartsWith(
                "TOE"))
        {
            return CarSetupCategory.Alignment;
        }

        if (
            value.Contains(
                "DAMP") ||
            value.Contains(
                "REBOUND"))
        {
            return CarSetupCategory.Dampers;
        }

        if (
            value.Contains(
                "SPRING") ||
            value.Contains(
                "ARB") ||
            value.Contains(
                "ROD_LENGTH") ||
            value.Contains(
                "PACKER") ||
            value.Contains(
                "BUMPSTOP"))
        {
            return CarSetupCategory.Suspension;
        }

        if (value.StartsWith(
                "DIFF"))
        {
            return CarSetupCategory.Differential;
        }

        if (
            value.Contains(
                "BRAKE") ||
            value.Contains(
                "BIAS"))
        {
            return CarSetupCategory.Brakes;
        }

        if (
            value.Contains(
                "RATIO") ||
            value.StartsWith(
                "GEAR"))
        {
            return CarSetupCategory.Gearing;
        }

        if (
            value.Contains(
                "WING") ||
            value.Contains(
                "AERO"))
        {
            return CarSetupCategory.Aero;
        }

        if (value ==
            "FUEL")
        {
            return CarSetupCategory.Fuel;
        }

        if (
            value is
                "ABS" or
                "TC" ||
            value.Contains(
                "TRACTION"))
        {
            return CarSetupCategory.Electronics;
        }

        return CarSetupCategory.Other;
    }
}
