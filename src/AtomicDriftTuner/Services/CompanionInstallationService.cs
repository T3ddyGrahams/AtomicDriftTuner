using System.Diagnostics;

namespace AtomicDriftTuner.Services;

/// <summary>Installs only ADT's packaged Lua app. Other apps and user files are never copied or removed.</summary>
public sealed class CompanionInstallationService
{
    public const string AppName = "ADTCompanion";
    public const string PackageDirectory = "CompanionPayload";
    public const string ImportArchiveName = "ADTCompanion-ContentManager.zip";
    public static IReadOnlyList<string> FileNames { get; } = Array.AsReadOnly(new[]
        { "ADTCompanion.lua", "companion_client.lua", "setup_capture.lua", "pit_setup.lua", "manifest.ini", "icon.png" });

    private readonly Func<bool> _isGameRunning;
    public CompanionInstallationService() : this(IsGameRunning) { }
    public CompanionInstallationService(Func<bool> isGameRunning) =>
        _isGameRunning = isGameRunning ?? throw new ArgumentNullException(nameof(isGameRunning));

    public sealed record InstallResult(string Folder, int ChangedFiles, string? BackupFolder);

    public static string PackagedAppFolder(string? applicationFolder = null) =>
        Path.Combine(applicationFolder ?? AppContext.BaseDirectory, PackageDirectory, "apps", "lua", AppName);

    public static string GetDestinationFolder(string? assettoCorsaRoot)
    {
        if (string.IsNullOrWhiteSpace(assettoCorsaRoot) || !Path.IsPathFullyQualified(assettoCorsaRoot))
            throw new InvalidOperationException("Choose the full Assetto Corsa install folder in Setup & Paths first.");
        var root = Path.GetFullPath(assettoCorsaRoot);
        EnsureNoLinks(root);
        if (!Directory.Exists(root) || !Directory.Exists(Path.Combine(root, "content", "cars")) ||
            !(File.Exists(Path.Combine(root, "acs.exe")) || File.Exists(Path.Combine(root, "acs_x86.exe"))))
            throw new InvalidOperationException("The selected folder is not an Assetto Corsa installation. Choose the folder containing acs.exe and content\\cars in Setup & Paths.");
        var destination = Path.Combine(root, "apps", "lua", AppName);
        EnsureNoLinks(destination);
        return destination;
    }

    public InstallResult Install(string? assettoCorsaRoot, string? packagedAppFolder = null)
    {
        var destination = GetDestinationFolder(assettoCorsaRoot);
        if (_isGameRunning())
            throw new InvalidOperationException("Exit the current Assetto Corsa driving session before installing or updating ADT Companion. Content Manager can stay open.");
        var source = Path.GetFullPath(packagedAppFolder ?? PackagedAppFolder());
        EnsureNoLinks(source);

        // Read and validate every source and destination before changing any installed file.
        var changes = new List<(string Name, byte[] Next, byte[]? Previous)>();
        foreach (var name in FileNames)
        {
            var sourceFile = Path.Combine(source, name);
            EnsureNoLinks(sourceFile);
            if (!File.Exists(sourceFile) || new FileInfo(sourceFile).Length is <= 0 or > 4 * 1024 * 1024)
                throw new InvalidOperationException("The packaged companion is missing or incomplete. Re-extract the full ADT portable ZIP or reinstall this ADT build.");
            var target = Path.Combine(destination, name);
            EnsureNoLinks(target);
            if (Directory.Exists(target)) throw new IOException("An ADT companion file path is occupied by a folder: " + name);
            if (File.Exists(target) && new FileInfo(target).Length > 16 * 1024 * 1024)
                throw new IOException("An existing ADT companion file is unexpectedly large. Back it up and check it before updating: " + name);
            var next = File.ReadAllBytes(sourceFile);
            var previous = File.Exists(target) ? File.ReadAllBytes(target) : null;
            if (previous is null || !previous.AsSpan().SequenceEqual(next)) changes.Add((name, next, previous));
        }
        if (changes.Count == 0) return new(destination, 0, null);
        if (_isGameRunning()) throw new InvalidOperationException("Assetto Corsa started while installation was being prepared. Exit the driving session and try again.");

        EnsureNoLinks(destination);
        Directory.CreateDirectory(destination);
        string? backup = null;
        if (changes.Any(c => c.Previous is not null))
        {
            backup = Path.Combine(destination, "_Backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            EnsureNoLinks(backup);
            Directory.CreateDirectory(backup);
            foreach (var change in changes.Where(c => c.Previous is not null))
                File.WriteAllBytes(Path.Combine(backup, change.Name), change.Previous!);
        }

        var replaced = new List<(string Name, byte[] Next, byte[]? Previous)>();
        try
        {
            foreach (var change in changes)
            {
                if (_isGameRunning()) throw new InvalidOperationException("Assetto Corsa started during installation. Exit the driving session and try again.");
                ReplaceFile(Path.Combine(destination, change.Name), change.Next);
                replaced.Add(change);
            }
        }
        catch (Exception installError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var change in replaced.AsEnumerable().Reverse())
            {
                try
                {
                    var target = Path.Combine(destination, change.Name);
                    EnsureNoLinks(target);
                    if (change.Previous is null) File.Delete(target);
                    else ReplaceFile(target, change.Previous);
                }
                catch (Exception ex) { rollbackErrors.Add(ex); }
            }
            if (rollbackErrors.Count > 0)
                throw new IOException("The companion update could not finish or fully restore its previous files. Previous files are kept in " + (backup ?? "the original app folder") + ". Reinstall after closing the game.", new AggregateException(new[] { installError }.Concat(rollbackErrors)));
            throw;
        }
        return new(destination, changes.Count, backup);
    }

    private static void ReplaceFile(string target, byte[] bytes)
    {
        EnsureNoLinks(target);
        var temporary = target + ".adt-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
            EnsureNoLinks(target);
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void EnsureNoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Companion installation cannot follow a symbolic link or junction. Choose a direct installation folder.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }

    private static bool IsGameRunning()
    {
        foreach (var name in new[] { "acs", "acs_x86" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }
}
