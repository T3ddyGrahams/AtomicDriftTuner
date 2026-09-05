using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed partial class AssettoCorsaScanner
{
    private const long MaximumUiCarJsonBytes =
        2 * 1024 * 1024;

    private const long MaximumPhysicsIniBytes =
        4 * 1024 * 1024;

    private const long MaximumSteamLibraryVdfBytes =
        4 * 1024 * 1024;

    private const int MaximumCarsToScan =
        5000;

    private const int MaximumMetadataTextLength =
        512;

    public string? TryFindInstall()
    {
        var candidates =
            new List<string>();

        TryAddProgramFilesCandidates(
            candidates);

        TryAddSteamRegistryCandidates(
            candidates);

        TryAddAssettoCorsaUninstallCandidate(
            candidates,
            RegistryView.Registry64);

        TryAddAssettoCorsaUninstallCandidate(
            candidates,
            RegistryView.Registry32);

        foreach (var candidate in candidates)
        {
            if (IsAssettoCorsaRoot(
                    candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public AssettoCorsaScanResult Scan(
        string rootOrCarsPath)
    {
        if (string.IsNullOrWhiteSpace(
                rootOrCarsPath))
        {
            throw new ArgumentException(
                "Choose an Assetto Corsa folder first.",
                nameof(rootOrCarsPath));
        }

        var root =
            NormalizeRoot(
                rootOrCarsPath);

        var carsPath =
            Path.Combine(
                root,
                "content",
                "cars");

        if (!Directory.Exists(
                carsPath))
        {
            throw new DirectoryNotFoundException(
                $"Could not find content\\cars under: {root}");
        }

        var result =
            new AssettoCorsaScanResult
            {
                RootPath =
                    root
            };

        var options =
            new EnumerationOptions
            {
                RecurseSubdirectories =
                    false,

                IgnoreInaccessible =
                    true,

                AttributesToSkip =
                    FileAttributes.ReparsePoint
            };

        var carDirectories =
            Directory
                .EnumerateDirectories(
                    carsPath,
                    "*",
                    options)
                .OrderBy(
                    path =>
                        path,
                    StringComparer.OrdinalIgnoreCase)
                .Take(
                    MaximumCarsToScan)
                .ToList();

        foreach (var directory in carDirectories)
        {
            try
            {
                result.Cars.Add(
                    ReadCar(
                        directory));
            }
            catch (Exception ex)
                when (IsRecoverableCarScanException(
                    ex))
            {
                result.Warnings.Add(
                    $"{SafeFileName(directory)}: {SafeWarning(ex)}");
            }
        }

        DiscoverFolderPrefixPacks(
            result);

        return result;
    }

    private static void DiscoverFolderPrefixPacks(
        AssettoCorsaScanResult result)
    {
        ArgumentNullException.ThrowIfNull(
            result);

        // Built-in signatures always win.
        //
        // Only otherwise-unknown cars participate in inferred pack grouping.
        var customCars =
            result.Cars
                .Where(
                    car =>
                        string.Equals(
                            car.PackId,
                            "custom-pack",
                            StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(
                            car.SourceFolderName))
                .ToList();

        if (customCars.Count < 2)
        {
            return;
        }

        var groups =
            customCars
                .Select(
                    car =>
                        new
                        {
                            Car =
                                car,

                            Tokens =
                                FolderTokens(
                                    car.SourceFolderName!)
                        })
                .Where(
                    item =>
                        item.Tokens.Count >=
                        3)
                .Select(
                    item =>
                        new
                        {
                            item.Car,

                            Prefix =
                                item.Tokens
                                    .Take(
                                        2)
                                    .ToArray()
                        })
                .Where(
                    item =>
                        IsUsefulPackPrefix(
                            item.Prefix))
                .GroupBy(
                    item =>
                        string.Join(
                            "_",
                            item.Prefix),
                    StringComparer.OrdinalIgnoreCase)
                .Where(
                    group =>
                        group.Count() >=
                        2)
                .OrderBy(
                    group =>
                        group.Key,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        foreach (var group in groups)
        {
            var prefix =
                group.First()
                    .Prefix;

            var slug =
                string.Join(
                    "-",
                    prefix.Select(
                        SlugToken));

            if (string.IsNullOrWhiteSpace(
                    slug))
            {
                continue;
            }

            var packId =
                $"auto-pack-{slug}";

            var packName =
                string.Join(
                    " ",
                    prefix.Select(
                        DisplayToken));

            if (
                result.DiscoveredPacks.Any(
                    pack =>
                        string.Equals(
                            pack.Id,
                            packId,
                            StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.DiscoveredPacks.Add(
                new DriftPackProfile
                {
                    Id =
                        packId,

                    Name =
                        packName,

                    Category =
                        "Auto-Detected",

                    GripBias =
                        0,

                    SelfSteerBias =
                        0,

                    DampingBias =
                        0,

                    DetailBias =
                        0,

                    IsCustom =
                        true
                });

            foreach (var item in group)
            {
                item.Car.PackId =
                    packId;

                item.Car.DataSourceSummary =
                    string.IsNullOrWhiteSpace(
                        item.Car.DataSourceSummary)
                        ? $"auto-pack: {packName}"
                        : $"{item.Car.DataSourceSummary}, auto-pack: {packName}";
            }
        }
    }

    private static List<string> FolderTokens(
        string folder)
    {
        if (string.IsNullOrWhiteSpace(
                folder))
        {
            return [];
        }

        return FolderTokenRegex()
            .Matches(
                folder)
            .Select(
                match =>
                    match.Value
                        .ToLowerInvariant())
            .Where(
                token =>
                    !string.IsNullOrWhiteSpace(
                        token))
            .Take(
                16)
            .ToList();
    }

    private static bool IsUsefulPackPrefix(
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return false;
        }

        // Avoid inferring nonsense "packs" from normal manufacturer/chassis
        // naming conventions.
        var genericFirstTokens =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "ac",
                "assetto",
                "assettocorsa",
                "car",
                "cars",
                "mod",
                "mods",
                "nissan",
                "toyota",
                "bmw",
                "mazda",
                "ford",
                "honda",
                "lexus",
                "mercedes",
                "benz",
                "porsche",
                "audi",
                "volkswagen",
                "vw",
                "chevrolet",
                "chevy",
                "dodge",
                "subaru",
                "mitsubishi",
                "ferrari",
                "lamborghini",
                "hyundai",
                "kia",
                "volvo"
            };

        if (genericFirstTokens.Contains(
                tokens[0]))
        {
            return false;
        }

        return tokens.All(
            token =>
                token.Length >= 2 &&
                token.Length <= 32 &&
                !token.All(
                    char.IsDigit));
    }

    private static string SlugToken(
        string token)
    {
        return new string(
            token
                .Where(
                    char.IsLetterOrDigit)
                .Select(
                    char.ToLowerInvariant)
                .Take(
                    32)
                .ToArray());
    }

    private static string DisplayToken(
        string token)
    {
        var acronyms =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "vdc",
                "vdm",
                "adl",
                "wdt",
                "wdts",
                "dwg",
                "bdc"
            };

        if (acronyms.Contains(
                token))
        {
            return token.ToUpperInvariant();
        }

        return CultureInfo
            .InvariantCulture
            .TextInfo
            .ToTitleCase(
                token.ToLowerInvariant());
    }

    private CarProfile ReadCar(
        string directory)
    {
        var fullDirectory =
            Path.GetFullPath(
                directory);

        var folder =
            SafeFileName(
                fullDirectory);

        if (string.IsNullOrWhiteSpace(
                folder))
        {
            throw new InvalidDataException(
                "Installed car folder has no usable name.");
        }

        var displayName =
            folder
                .Replace(
                    '_',
                    ' ')
                .Trim();

        var author =
            string.Empty;

        var brand =
            string.Empty;

        var tags =
            string.Empty;

        double? mass =
            null;

        double? power =
            null;

        double? torque =
            null;

        var sources =
            new List<string>();

        var uiJson =
            Path.Combine(
                fullDirectory,
                "ui",
                "ui_car.json");

        if (File.Exists(
                uiJson))
        {
            try
            {
                using var document =
                    ParseJsonFileBounded(
                        uiJson,
                        MaximumUiCarJsonBytes);

                var root =
                    document.RootElement;

                displayName =
                    NormalizeMetadataText(
                        GetString(
                            root,
                            "name")) ??
                    displayName;

                author =
                    NormalizeMetadataText(
                        GetString(
                            root,
                            "author")) ??
                    string.Empty;

                brand =
                    NormalizeMetadataText(
                        GetString(
                            root,
                            "brand")) ??
                    string.Empty;

                if (root.TryGetProperty(
                        "tags",
                        out var tagElement))
                {
                    tags =
                        NormalizeMetadataText(
                            tagElement.ToString()) ??
                        string.Empty;
                }

                if (
                    root.TryGetProperty(
                        "specs",
                        out var specs) &&
                    specs.ValueKind ==
                    JsonValueKind.Object)
                {
                    power =
                        ParseNumber(
                            GetString(
                                specs,
                                "bhp") ??
                            GetString(
                                specs,
                                "power"));

                    mass =
                        ParseNumber(
                            GetString(
                                specs,
                                "weight"));

                    torque =
                        ParseNumber(
                            GetString(
                                specs,
                                "torque"));
                }

                sources.Add(
                    "ui_car.json");
            }
            catch (Exception ex)
                when (IsRecoverableMetadataException(
                    ex))
            {
                sources.Add(
                    "folder name (ui_car.json unreadable)");
            }
        }

        var searchable =
            string.Join(
                " ",
                new[]
                {
                    folder,
                    displayName,
                    author,
                    brand,
                    tags
                }
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value)));

        var packId =
            InferPack(
                searchable);

        var car =
            DefaultsForPack(
                packId);

        car.Id =
            $"installed:{folder}";

        car.PackId =
            packId;

        car.DisplayName =
            displayName;

        car.IsInstalled =
            true;

        car.SourceFolderName =
            folder;

        car.SourceFolderPath =
            fullDirectory;

        car.Author =
            string.IsNullOrWhiteSpace(
                author)
                ? null
                : author;

        if (
            mass is >= 500 and <= 3000)
        {
            car.MassKg =
                mass.Value;

            car.Confidence.Mass =
                DataConfidence.High;
        }

        if (
            power is >= 50 and <= 2500)
        {
            car.PowerHp =
                power.Value;

            car.Confidence.Power =
                DataConfidence.High;
        }

        if (
            torque is >= 50 and <= 3000)
        {
            car.TorqueNm =
                torque.Value;
        }

        car.Confidence.Grip =
            string.Equals(
                packId,
                "custom-pack",
                StringComparison.OrdinalIgnoreCase)
                ? DataConfidence.Low
                : DataConfidence.Medium;

        car.Confidence.Caster =
            DataConfidence.Low;

        ReadCarIni(
            fullDirectory,
            car,
            sources);

        ReadTyresIni(
            fullDirectory,
            car,
            sources);

        var dataDirectory =
            Path.Combine(
                fullDirectory,
                "data");

        var packedData =
            Path.Combine(
                fullDirectory,
                "data.acd");

        if (
            File.Exists(
                packedData) &&
            !Directory.Exists(
                dataDirectory))
        {
            sources.Add(
                "data.acd present; packed physics not read");
        }

        car.DataSourceSummary =
            sources.Count == 0
                ? "folder-name fallback"
                : string.Join(
                    ", ",
                    sources.Distinct(
                        StringComparer.OrdinalIgnoreCase));

        return car;
    }

    private static void ReadCarIni(
        string carDirectory,
        CarProfile car,
        List<string> sources)
    {
        var carIni =
            Path.Combine(
                carDirectory,
                "data",
                "car.ini");

        if (!File.Exists(
                carIni))
        {
            return;
        }

        try
        {
            var ini =
                ReadIni(
                    carIni,
                    MaximumPhysicsIniBytes);

            var totalMass =
                IniNumber(
                    ini,
                    "BASIC",
                    "TOTALMASS");

            if (
                totalMass is >= 500 and <= 3000)
            {
                car.MassKg =
                    totalMass.Value;

                car.Confidence.Mass =
                    DataConfidence.High;
            }

            // Some mods expose road-wheel lock here while others expose
            // steering-wheel values. Only accept plausible per-side
            // road-wheel values.
            var steerLock =
                IniNumber(
                    ini,
                    "CONTROLS",
                    "STEER_LOCK");

            if (
                steerLock is >= 20 and <= 80)
            {
                car.SteeringLockPerSideDeg =
                    steerLock.Value;

                car.Confidence.SteeringLock =
                    DataConfidence.High;
            }

            sources.Add(
                "data/car.ini");
        }
        catch (Exception ex)
            when (IsRecoverableMetadataException(
                ex))
        {
            sources.Add(
                "data/car.ini unreadable");
        }
    }

    private static void ReadTyresIni(
        string carDirectory,
        CarProfile car,
        List<string> sources)
    {
        var tyresIni =
            Path.Combine(
                carDirectory,
                "data",
                "tyres.ini");

        if (!File.Exists(
                tyresIni))
        {
            return;
        }

        try
        {
            var ini =
                ReadIni(
                    tyresIni,
                    MaximumPhysicsIniBytes);

            var width =
                IniNumber(
                    ini,
                    "FRONT",
                    "WIDTH");

            if (
                width is > 0 and < 1)
            {
                width *=
                    1000;
            }

            if (
                width is >= 150 and <= 400)
            {
                car.FrontTireWidthMm =
                    width.Value;

                car.Confidence.FrontTireWidth =
                    DataConfidence.High;
            }

            sources.Add(
                "data/tyres.ini");
        }
        catch (Exception ex)
            when (IsRecoverableMetadataException(
                ex))
        {
            sources.Add(
                "data/tyres.ini unreadable");
        }
    }

    private static CarProfile DefaultsForPack(
        string packId)
    {
        return packId switch
        {
            "vdc" =>
                new CarProfile
                {
                    MassKg =
                        1350,

                    PowerHp =
                        900,

                    TorqueNm =
                        950,

                    SteeringLockPerSideDeg =
                        65,

                    CasterDeg =
                        8.5,

                    FrontTireWidthMm =
                        275,

                    RearTireWidthMm =
                        285,

                    Grip =
                        GripLevel.High
                },

            "adl" =>
                new CarProfile
                {
                    MassKg =
                        1400,

                    PowerHp =
                        900,

                    TorqueNm =
                        950,

                    SteeringLockPerSideDeg =
                        65,

                    CasterDeg =
                        8.5,

                    FrontTireWidthMm =
                        275,

                    RearTireWidthMm =
                        285,

                    Grip =
                        GripLevel.High
                },

            "gravy" =>
                new CarProfile
                {
                    MassKg =
                        1250,

                    PowerHp =
                        350,

                    TorqueNm =
                        480,

                    SteeringLockPerSideDeg =
                        60,

                    CasterDeg =
                        7.0,

                    FrontTireWidthMm =
                        245,

                    RearTireWidthMm =
                        255,

                    Grip =
                        GripLevel.Medium
                },

            "swarm" =>
                new CarProfile
                {
                    MassKg =
                        1300,

                    PowerHp =
                        450,

                    TorqueNm =
                        520,

                    SteeringLockPerSideDeg =
                        60,

                    CasterDeg =
                        7.5,

                    FrontTireWidthMm =
                        245,

                    RearTireWidthMm =
                        265,

                    Grip =
                        GripLevel.Medium
                },

            "wdts" =>
                new CarProfile
                {
                    MassKg =
                        1250,

                    PowerHp =
                        400,

                    TorqueNm =
                        480,

                    SteeringLockPerSideDeg =
                        60,

                    CasterDeg =
                        7.0,

                    FrontTireWidthMm =
                        235,

                    RearTireWidthMm =
                        245,

                    Grip =
                        GripLevel.Medium
                },

            "dwg" =>
                new CarProfile
                {
                    MassKg =
                        1300,

                    PowerHp =
                        430,

                    TorqueNm =
                        510,

                    SteeringLockPerSideDeg =
                        60,

                    CasterDeg =
                        7.3,

                    FrontTireWidthMm =
                        245,

                    RearTireWidthMm =
                        255,

                    Grip =
                        GripLevel.Medium
                },

            _ =>
                new CarProfile
                {
                    MassKg =
                        1300,

                    PowerHp =
                        400,

                    TorqueNm =
                        450,

                    SteeringLockPerSideDeg =
                        60,

                    CasterDeg =
                        7.0,

                    FrontTireWidthMm =
                        265,

                    RearTireWidthMm =
                        265,

                    Grip =
                        GripLevel.Medium,

                    IsCustom =
                        true
                }
        };
    }

    private static string InferPack(
        string text)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return "custom-pack";
        }

        var searchable =
            text.ToLowerInvariant();

        if (ContainsPackSignature(
                searchable,
                "virtual drift championship",
                "vdc"))
        {
            return "vdc";
        }

        if (ContainsPackSignature(
                searchable,
                "gravy garage",
                "gravygarage",
                "gravy"))
        {
            return "gravy";
        }

        if (ContainsPackSignature(
                searchable,
                "team swarm",
                "teamswarm",
                "swarm"))
        {
            return "swarm";
        }

        if (ContainsPackSignature(
                searchable,
                "assetto drift league",
                "adl"))
        {
            return "adl";
        }

        if (ContainsPackSignature(
                searchable,
                "world drift tour",
                "wdts",
                "wdt"))
        {
            return "wdts";
        }

        if (ContainsPackSignature(
                searchable,
                "deathwish garage",
                "deathwish",
                "dwg"))
        {
            return "dwg";
        }

        return "custom-pack";
    }

    private static bool ContainsPackSignature(
        string text,
        params string[] signatures)
    {
        foreach (var signature in signatures)
        {
            if (string.IsNullOrWhiteSpace(
                    signature))
            {
                continue;
            }

            if (signature.Contains(
                    ' '))
            {
                if (text.Contains(
                        signature,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (ContainsToken(
                    text,
                    signature))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsToken(
        string text,
        string token)
    {
        if (string.IsNullOrWhiteSpace(
                token))
        {
            return false;
        }

        var matches =
            FolderTokenRegex()
                .Matches(
                    text);

        return matches.Any(
            match =>
                string.Equals(
                    match.Value,
                    token,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRoot(
        string path)
    {
        string full;

        try
        {
            full =
                Path.GetFullPath(
                    Environment
                        .ExpandEnvironmentVariables(
                            path
                                .Trim()
                                .Trim('"')));
        }
        catch (Exception ex)
            when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
        {
            throw new InvalidDataException(
                "ADT could not normalize the selected Assetto Corsa path.",
                ex);
        }

        if (Directory.Exists(
                Path.Combine(
                    full,
                    "content",
                    "cars")))
        {
            return full;
        }

        DirectoryInfo directoryInfo;

        try
        {
            directoryInfo =
                new DirectoryInfo(
                    full);
        }
        catch (Exception ex)
            when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
        {
            throw new InvalidDataException(
                "ADT could not inspect the selected Assetto Corsa path.",
                ex);
        }

        if (
            directoryInfo.Name.Equals(
                "cars",
                StringComparison.OrdinalIgnoreCase) &&
            directoryInfo.Parent?.Name.Equals(
                "content",
                StringComparison.OrdinalIgnoreCase) ==
            true &&
            directoryInfo.Parent.Parent is not null)
        {
            return directoryInfo
                .Parent
                .Parent
                .FullName;
        }

        return full;
    }

    private static bool IsAssettoCorsaRoot(
        string path)
    {
        try
        {
            return
                !string.IsNullOrWhiteSpace(
                    path) &&
                Directory.Exists(
                    Path.Combine(
                        path,
                        "content",
                        "cars"));
        }
        catch
        {
            return false;
        }
    }

    private static void TryAddProgramFilesCandidates(
        List<string> candidates)
    {
        AddIfPresent(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86),
                "Steam",
                "steamapps",
                "common",
                "assettocorsa"));

        AddIfPresent(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),
                "Steam",
                "steamapps",
                "common",
                "assettocorsa"));
    }

    private static void TryAddSteamRegistryCandidates(
        List<string> candidates)
    {
        try
        {
            using var baseKey =
                RegistryKey.OpenBaseKey(
                    RegistryHive.CurrentUser,
                    RegistryView.Default);

            using var key =
                baseKey.OpenSubKey(
                    @"Software\Valve\Steam");

            var steamPath =
                key?.GetValue(
                        "SteamPath")
                    ?.ToString();

            if (string.IsNullOrWhiteSpace(
                    steamPath))
            {
                return;
            }

            AddIfPresent(
                candidates,
                Path.Combine(
                    steamPath,
                    "steamapps",
                    "common",
                    "assettocorsa"));

            AddSteamLibraries(
                candidates,
                steamPath);
        }
        catch (
            Exception ex)
            when (
                ex is UnauthorizedAccessException ||
                ex is IOException ||
                ex is System.Security.SecurityException ||
                ex is ArgumentException)
        {
            // Install auto-detection is best effort.
        }
    }

    private static void TryAddAssettoCorsaUninstallCandidate(
        List<string> candidates,
        RegistryView view)
    {
        try
        {
            using var baseKey =
                RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    view);

            using var key =
                baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 244210");

            var install =
                key?.GetValue(
                        "InstallLocation")
                    ?.ToString();

            AddIfPresent(
                candidates,
                install);
        }
        catch (
            Exception ex)
            when (
                ex is UnauthorizedAccessException ||
                ex is IOException ||
                ex is System.Security.SecurityException ||
                ex is ArgumentException)
        {
            // Registry discovery is optional; manual path selection remains.
        }
    }

    private static void AddIfPresent(
        List<string> list,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return;
        }

        string full;

        try
        {
            full =
                Path.GetFullPath(
                    Environment
                        .ExpandEnvironmentVariables(
                            path.Trim()));
        }
        catch (
            Exception ex)
            when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
        {
            return;
        }

        if (!Directory.Exists(
                full))
        {
            return;
        }

        if (
            list.Contains(
                full,
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        list.Add(
            full);
    }

    private static void AddSteamLibraries(
        List<string> candidates,
        string steamPath)
    {
        var vdf =
            Path.Combine(
                steamPath,
                "steamapps",
                "libraryfolders.vdf");

        if (!File.Exists(
                vdf))
        {
            return;
        }

        try
        {
            foreach (var line in
                     ReadLinesBounded(
                         vdf,
                         MaximumSteamLibraryVdfBytes))
            {
                var match =
                    SteamLibraryPathRegex()
                        .Match(
                            line);

                if (!match.Success)
                {
                    continue;
                }

                var library =
                    match
                        .Groups["p"]
                        .Value
                        .Replace(
                            "\\\\",
                            "\\");

                AddIfPresent(
                    candidates,
                    Path.Combine(
                        library,
                        "steamapps",
                        "common",
                        "assettocorsa"));
            }
        }
        catch (Exception ex)
            when (IsRecoverableMetadataException(
                ex))
        {
            // Steam library discovery is best effort.
        }
    }

    private static string? GetString(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out var value))
        {
            return null;
        }

        return value.ValueKind ==
               JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static double? ParseNumber(
        string? text)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return null;
        }

        var normalized =
            text.Replace(
                ',',
                '.');

        var match =
            NumberRegex()
                .Match(
                    normalized);

        if (!match.Success)
        {
            return null;
        }

        if (!double.TryParse(
                match.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return null;
        }

        return double.IsFinite(
                value)
            ? value
            : null;
    }

    private static Dictionary<
        string,
        Dictionary<string, string>> ReadIni(
        string path,
        long maximumBytes)
    {
        var result =
            new Dictionary<
                string,
                Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);

        var section =
            string.Empty;

        foreach (var raw in
                 ReadLinesBounded(
                     path,
                     maximumBytes))
        {
            var line =
                raw.Trim();

            if (
                line.Length == 0 ||
                line.StartsWith(
                    ';') ||
                line.StartsWith(
                    '#'))
            {
                continue;
            }

            if (
                line.StartsWith(
                    '[') &&
                line.EndsWith(
                    ']'))
            {
                section =
                    line[1..^1]
                        .Trim();

                if (
                    section.Length > 0 &&
                    !result.ContainsKey(
                        section))
                {
                    result[section] =
                        new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            var equals =
                line.IndexOf('=');

            if (
                equals <= 0 ||
                string.IsNullOrWhiteSpace(
                    section))
            {
                continue;
            }

            var key =
                line[..equals]
                    .Trim();

            if (key.Length == 0)
            {
                continue;
            }

            var value =
                line[(equals + 1)..]
                    .Split(
                        ';',
                        2)[0]
                    .Trim();

            result[section][key] =
                value;
        }

        return result;
    }

    private static double? IniNumber(
        Dictionary<
            string,
            Dictionary<string, string>> ini,
        string section,
        string key)
    {
        if (
            !ini.TryGetValue(
                section,
                out var values) ||
            !values.TryGetValue(
                key,
                out var raw))
        {
            return null;
        }

        return ParseNumber(
            raw);
    }

    private static JsonDocument ParseJsonFileBounded(
        string path,
        long maximumBytes)
    {
        EnsureFileSize(
            path,
            maximumBytes);

        using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

        return JsonDocument.Parse(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas =
                    true,

                CommentHandling =
                    JsonCommentHandling.Skip,

                MaxDepth =
                    64
            });
    }

    private static IEnumerable<string> ReadLinesBounded(
        string path,
        long maximumBytes)
    {
        EnsureFileSize(
            path,
            maximumBytes);

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
            if (stream.Position >
                maximumBytes)
            {
                throw new InvalidDataException(
                    $"ADT refused to read '{path}' because the file exceeded the {maximumBytes:N0}-byte safety limit.");
            }

            yield return line;
        }
    }

    private static void EnsureFileSize(
        string path,
        long maximumBytes)
    {
        var info =
            new FileInfo(
                path);

        if (
            info.Exists &&
            info.Length >
            maximumBytes)
        {
            throw new InvalidDataException(
                $"ADT refused to read '{path}' because the file is unexpectedly large ({info.Length:N0} bytes).");
        }
    }

    private static string? NormalizeMetadataText(
        string? value)
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
                    .Take(
                        MaximumMetadataTextLength)
                    .ToArray());

        return cleaned.Length == 0
            ? null
            : cleaned;
    }

    private static string SafeFileName(
        string path)
    {
        try
        {
            return
                Path.GetFileName(
                    Path.TrimEndingDirectorySeparator(
                        path));
        }
        catch
        {
            return "unknown-car";
        }
    }

    private static string SafeWarning(
        Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException =>
                "Access denied while reading car data.",

            IOException =>
                "A file or folder could not be read.",

            JsonException =>
                "ui_car.json contains invalid JSON.",

            InvalidDataException =>
                exception.Message,

            _ =>
                "Car metadata could not be read."
        };
    }

    private static bool IsRecoverableCarScanException(
        Exception exception)
    {
        return exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException or
            ArgumentException or
            NotSupportedException;
    }

    private static bool IsRecoverableMetadataException(
        Exception exception)
    {
        return exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException or
            ArgumentException or
            NotSupportedException;
    }

    [GeneratedRegex(
        @"[A-Za-z0-9]+",
        RegexOptions.Compiled)]
    private static partial Regex FolderTokenRegex();

    [GeneratedRegex(
        @"[-+]?\d+(?:\.\d+)?",
        RegexOptions.Compiled)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(
        "\"path\"\\s+\"(?<p>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SteamLibraryPathRegex();
}
