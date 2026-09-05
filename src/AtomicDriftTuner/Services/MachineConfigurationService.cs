using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class MachineConfigurationService
{
    private const string SimHubExecutableName =
        "SimHubWPF.exe";

    private const string AssettoCorsaDirectoryName =
        "Assetto Corsa";

    private readonly AssettoCorsaScanner _scanner;

    public MachineConfigurationService()
        : this(new AssettoCorsaScanner())
    {
    }

    internal MachineConfigurationService(
        AssettoCorsaScanner scanner)
    {
        _scanner =
            scanner ??
            throw new ArgumentNullException(
                nameof(scanner));
    }

    public MachineDetectionResult Detect(
        AppSettings? saved = null)
    {
        saved ??=
            new AppSettings();

        var savedSimHubCandidate =
            FirstNonEmpty(
                saved.SimHubRoot,
                GetSimHubRootFromExecutable(
                    saved.AzomLive?.SimHubExePath));

        var simHub =
            NormalizeExistingDirectory(
                SimHubLocator.FindSimHubRoot(
                    savedSimHubCandidate));

        var savedAcRoot =
            NormalizeDirectory(
                saved.AssettoCorsaRoot);

        var acRoot =
            ValidateAssettoCorsaRoot(
                savedAcRoot)
                ? savedAcRoot
                : NormalizeExistingDirectory(
                    _scanner.TryFindInstall());

        var savedAcDocuments =
            NormalizeDirectory(
                saved.AssettoCorsaDocumentsRoot);

        var acDocuments =
            ValidateAssettoCorsaDocumentsRoot(
                savedAcDocuments)
                ? savedAcDocuments
                : FindAssettoCorsaDocumentsRoot();

        return new MachineDetectionResult
        {
            SimHubRoot =
                simHub,

            AssettoCorsaRoot =
                acRoot,

            AssettoCorsaDocumentsRoot =
                acDocuments,

            SimHubValid =
                SimHubLocator.IsValidRoot(
                    simHub),

            AssettoCorsaValid =
                ValidateAssettoCorsaRoot(
                    acRoot),

            AssettoCorsaDocumentsValid =
                ValidateAssettoCorsaDocumentsRoot(
                    acDocuments)
        };
    }

    public string? FindAssettoCorsaDocumentsRoot()
    {
        // Environment.SpecialFolder.MyDocuments uses the Windows known-folder
        // location and therefore respects redirected Documents locations,
        // including common OneDrive-backed configurations.
        var documents =
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);

        var normalizedDocuments =
            NormalizeDirectory(
                documents);

        if (normalizedDocuments is null)
        {
            return null;
        }

        // Return the expected AC Documents location even if Assetto Corsa has
        // not created it yet. The caller separately receives a validity flag.
        return NormalizeDirectory(
            Path.Combine(
                normalizedDocuments,
                AssettoCorsaDirectoryName));
    }

    public bool ValidateAssettoCorsaRoot(
        string? root)
    {
        var normalized =
            NormalizeDirectory(
                root);

        if (normalized is null)
        {
            return false;
        }

        try
        {
            if (!Directory.Exists(normalized))
            {
                return false;
            }

            var contentDirectory =
                Path.Combine(
                    normalized,
                    "content");

            var carsDirectory =
                Path.Combine(
                    contentDirectory,
                    "cars");

            return
                Directory.Exists(contentDirectory) &&
                Directory.Exists(carsDirectory);
        }
        catch
        {
            return false;
        }
    }

    public bool ValidateAssettoCorsaDocumentsRoot(
        string? root)
    {
        var normalized =
            NormalizeDirectory(
                root);

        if (normalized is null)
        {
            return false;
        }

        try
        {
            return Directory.Exists(
                normalized);
        }
        catch
        {
            return false;
        }
    }

    public void ApplyToSettings(
        AppSettings settings,
        string? simHubRoot,
        string? acRoot,
        string? acDocumentsRoot,
        bool markFirstRunComplete)
    {
        ArgumentNullException.ThrowIfNull(
            settings);

        var normalizedSimHubRoot =
            NormalizeDirectory(
                simHubRoot);

        var normalizedAcRoot =
            NormalizeDirectory(
                acRoot);

        var normalizedAcDocumentsRoot =
            NormalizeDirectory(
                acDocumentsRoot);

        settings.SimHubRoot =
            normalizedSimHubRoot;

        settings.AssettoCorsaRoot =
            normalizedAcRoot;

        settings.AssettoCorsaDocumentsRoot =
            normalizedAcDocumentsRoot;

        settings.AzomLive ??=
            new AzomLiveConnectionSettings();

        settings.AzomLive.SimHubExePath =
            normalizedSimHubRoot is not null
                ? Path.Combine(
                    normalizedSimHubRoot,
                    SimHubExecutableName)
                : null;

        if (markFirstRunComplete)
        {
            settings.FirstRunCompleted =
                true;
        }
    }

    private static string? NormalizeDirectory(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var expanded =
                Environment.ExpandEnvironmentVariables(
                    path.Trim().Trim('"'));

            if (string.IsNullOrWhiteSpace(expanded))
            {
                return null;
            }

            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    expanded));
        }
        catch
        {
            // Invalid paths are never persisted back into ADT settings.
            return null;
        }
    }

    private static string? NormalizeExistingDirectory(
        string? path)
    {
        var normalized =
            NormalizeDirectory(
                path);

        if (normalized is null)
        {
            return null;
        }

        try
        {
            return Directory.Exists(
                normalized)
                ? normalized
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetSimHubRootFromExecutable(
        string? executablePath)
    {
        var normalized =
            NormalizeDirectory(
                executablePath);

        if (normalized is null)
        {
            return null;
        }

        try
        {
            var fileName =
                Path.GetFileName(
                    normalized);

            if (!string.Equals(
                    fileName,
                    SimHubExecutableName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Path.GetDirectoryName(
                normalized);
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstNonEmpty(
        params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
