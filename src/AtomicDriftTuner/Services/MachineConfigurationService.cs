using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class MachineConfigurationService
{
    private readonly AssettoCorsaScanner _scanner = new();

    public MachineDetectionResult Detect(AppSettings? saved = null)
    {
        saved ??= new AppSettings();

        string? simHub =
            SimHubLocator.FindSimHubRoot(
                saved.SimHubRoot ??
                saved.AzomLive?.SimHubExePath);

        string? acRoot =
            ValidateAssettoCorsaRoot(saved.AssettoCorsaRoot)
                ? Path.GetFullPath(saved.AssettoCorsaRoot!)
                : _scanner.TryFindInstall();

        string? acDocuments =
            ValidateAssettoCorsaDocumentsRoot(saved.AssettoCorsaDocumentsRoot)
                ? Path.GetFullPath(saved.AssettoCorsaDocumentsRoot!)
                : FindAssettoCorsaDocumentsRoot();

        return new MachineDetectionResult
        {
            SimHubRoot = simHub,
            AssettoCorsaRoot = acRoot,
            AssettoCorsaDocumentsRoot = acDocuments,
            SimHubValid = SimHubLocator.IsValidRoot(simHub),
            AssettoCorsaValid = ValidateAssettoCorsaRoot(acRoot),
            AssettoCorsaDocumentsValid = ValidateAssettoCorsaDocumentsRoot(acDocuments)
        };
    }

    public string? FindAssettoCorsaDocumentsRoot()
    {
        // Environment.SpecialFolder.MyDocuments uses the Windows known-folder
        // location, so redirected/OneDrive Documents folders are respected.
        var documents =
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);

        if (string.IsNullOrWhiteSpace(documents))
            return null;

        var candidate =
            Path.Combine(
                documents,
                "Assetto Corsa");

        return Directory.Exists(candidate)
            ? candidate
            : candidate; // Return the expected location even before AC creates it.
    }

    public bool ValidateAssettoCorsaRoot(string? root) =>
        !string.IsNullOrWhiteSpace(root) &&
        Directory.Exists(Path.Combine(root, "content", "cars"));

    public bool ValidateAssettoCorsaDocumentsRoot(string? root) =>
        !string.IsNullOrWhiteSpace(root) &&
        Directory.Exists(root);

    public void ApplyToSettings(
        AppSettings settings,
        string? simHubRoot,
        string? acRoot,
        string? acDocumentsRoot,
        bool markFirstRunComplete)
    {
        settings.SimHubRoot = CleanDirectory(simHubRoot);
        settings.AssettoCorsaRoot = CleanDirectory(acRoot);
        settings.AssettoCorsaDocumentsRoot = CleanDirectory(acDocumentsRoot);

        settings.AzomLive ??= new AzomLiveConnectionSettings();
        settings.AzomLive.SimHubExePath =
            !string.IsNullOrWhiteSpace(settings.SimHubRoot)
                ? Path.Combine(settings.SimHubRoot, "SimHubWPF.exe")
                : null;

        if (markFirstRunComplete)
            settings.FirstRunCompleted = true;
    }

    private static string? CleanDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(
                    path.Trim().Trim('"')));
        }
        catch
        {
            return path.Trim().Trim('"');
        }
    }
}
