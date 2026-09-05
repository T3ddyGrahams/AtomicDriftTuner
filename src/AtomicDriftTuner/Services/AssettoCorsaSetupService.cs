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
        CarProfile car)
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
                car);

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

            if (
                string.IsNullOrWhiteSpace(section) ||
                !line.StartsWith(
                    "VALUE=",
                    StringComparison.OrdinalIgnoreCase))
            {
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

        return new CarSetupAnalysis
        {
            BaselinePath =
                fullPath,

            CarFolderName =
                string.IsNullOrWhiteSpace(
                    car.SourceFolderName)
                    ? "unknown-car"
                    : car.SourceFolderName.Trim(),

            SetupDefinitionPath =
                FindSetupDefinition(
                    car),

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
                outputLines.Add(
                    $"VALUE={replacement}");
            }
            else
            {
                outputLines.Add(
                    rawLine);
            }
        }

        WriteAllLinesAtomic(
            outputFull,
            outputLines);

        return outputFull;
    }

    private Dictionary<string, SetupRangeDefinition> LoadDefinitions(
        CarProfile car)
    {
        var path =
            FindSetupDefinition(
                car);

        var result =
            new Dictionary<
                string,
                SetupRangeDefinition>(
                StringComparer.OrdinalIgnoreCase);

        if (path is null)
        {
            return result;
        }

        EnsureFileSize(
            path,
            MaximumDefinitionBytes,
            "Assetto Corsa setup definition");

        var raw =
            new Dictionary<
                string,
                Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);

        var section =
            string.Empty;

        foreach (var rawLine in
                 ReadAllLinesBounded(
                     path,
                     MaximumDefinitionBytes))
        {
            var line =
                rawLine.Trim();

            if (
                line.Length == 0 ||
                line.StartsWith(
                    ';'))
            {
                continue;
            }

            if (TryReadSectionHeader(
                    line,
                    out var parsedSection))
            {
                section =
                    parsedSection;

                if (!raw.ContainsKey(
                        section))
                {
                    raw[section] =
                        new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            var equalsIndex =
                line.IndexOf('=');

            if (
                equalsIndex <= 0 ||
                string.IsNullOrWhiteSpace(
                    section))
            {
                continue;
            }

            var key =
                line[..equalsIndex]
                    .Trim();

            if (key.Length == 0)
            {
                continue;
            }

            var value =
                line[(equalsIndex + 1)..]
                    .Split(
                        ';',
                        2)[0]
                    .Trim();

            raw[section][key] =
                value;
        }

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

            var values =
                pair.Value;

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
                        "data/setup.ini"
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
