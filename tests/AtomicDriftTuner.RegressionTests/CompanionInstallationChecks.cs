using System.IO;
using AtomicDriftTuner.Services;

internal static class CompanionInstallationChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static void Run(Action<string, Action> run, string root)
    {
        run("companion installation copies only shipped app files and preserves other mods", () =>
        {
            var (game, source) = Fixture(root, "companion-install");
            File.WriteAllText(Path.Combine(source, "pairing-token.json"), "never package or copy this");
            var other = Path.Combine(game, "apps", "lua", "AnotherApp", "app.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(other)!); File.WriteAllText(other, "keep other mod");
            var result = new CompanionInstallationService(() => false).Install(game, source);
            Check(result.ChangedFiles == 5 && result.BackupFolder is null, "First install did not report five new files");
            Check(Directory.GetFiles(result.Folder).Length == 5, "Installer copied an unlisted file");
            Check(File.ReadAllText(other) == "keep other mod", "Installer changed another app");
            foreach (var name in CompanionInstallationService.FileNames)
                Check(File.ReadAllBytes(Path.Combine(result.Folder, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(source, name))), "Installed bytes differ: " + name);
        });

        run("companion updates back up changed owned files and leave local settings intact", () =>
        {
            var (game, source) = Fixture(root, "companion-update");
            var service = new CompanionInstallationService(() => false);
            var first = service.Install(game, source);
            var original = File.ReadAllText(Path.Combine(first.Folder, "ADTCompanion.lua"));
            File.WriteAllText(Path.Combine(first.Folder, "settings.ini"), "keep my settings");
            Check(service.Install(game, source).ChangedFiles == 0, "Unchanged install wrote files");
            Check(!Directory.Exists(Path.Combine(first.Folder, "_Backups")), "Unchanged install made backups");
            File.WriteAllText(Path.Combine(source, "ADTCompanion.lua"), "updated fixture");
            var update = service.Install(game, source);
            Check(update.ChangedFiles == 1 && update.BackupFolder is not null, "Update did not report its change/backup");
            Check(File.ReadAllText(Path.Combine(update.BackupFolder!, "ADTCompanion.lua")) == original, "Original app was not backed up");
            Check(Directory.GetFiles(update.BackupFolder!).Length == 1, "Unchanged files were backed up");
            Check(File.ReadAllText(Path.Combine(update.Folder, "settings.ini")) == "keep my settings", "App-local user setting was changed");
            Check(service.Install(game, source).ChangedFiles == 0, "Repeated update wrote files again");
        });

        run("companion installation rejects invalid paths and incomplete packages before writes", () =>
        {
            var (game, source) = Fixture(root, "companion-invalid");
            var service = new CompanionInstallationService(() => false);
            foreach (var invalid in new string?[] { null, "", "relative-game", source })
                Reject(() => service.Install(invalid, source), "Invalid AC root accepted");
            foreach (var missing in new[] { "manifest.ini", "setup_capture.lua" })
            {
                var path = Path.Combine(source, missing);
                var bytes = File.ReadAllBytes(path);
                File.Delete(path);
                Reject(() => service.Install(game, source), "Incomplete package accepted: " + missing);
                Check(!Directory.Exists(Path.Combine(game, "apps")), "Failed validation wrote into the game");
                File.WriteAllBytes(path, bytes);
            }
        });

        run("companion installation leaves a running driving session untouched", () =>
        {
            var (game, source) = Fixture(root, "companion-running");
            Reject(() => new CompanionInstallationService(() => true).Install(game, source), "Running AC was accepted");
            Check(!Directory.Exists(Path.Combine(game, "apps")), "Running game installation was modified");
        });

        run("companion update rolls back if AC starts during file replacement", () =>
        {
            var (game, source) = Fixture(root, "companion-rollback");
            var installed = new CompanionInstallationService(() => false).Install(game, source).Folder;
            var previous = CompanionInstallationService.FileNames.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(installed, n)));
            foreach (var name in CompanionInstallationService.FileNames) File.WriteAllText(Path.Combine(source, name), "new fixture " + name);
            var checks = 0;
            Reject(() => new CompanionInstallationService(() => ++checks >= 4).Install(game, source), "Game startup during update was ignored");
            Check(checks == 4, "Fixture did not interrupt after the first replacement");
            foreach (var name in CompanionInstallationService.FileNames)
                Check(File.ReadAllBytes(Path.Combine(installed, name)).SequenceEqual(previous[name]), "Partial update was not rolled back: " + name);
            Check(Directory.GetFiles(installed, "*.tmp").Length == 0, "Temporary install file leaked");
            Check(Directory.GetDirectories(Path.Combine(installed, "_Backups")).Length == 1, "Original files are not retained for recovery");
        });

        run("companion installation rejects occupied target folders without changing earlier files", () =>
        {
            var (game, source) = Fixture(root, "companion-occupied");
            var destination = CompanionInstallationService.GetDestinationFolder(game);
            Directory.CreateDirectory(Path.Combine(destination, "icon.png"));
            File.WriteAllText(Path.Combine(destination, "ADTCompanion.lua"), "keep existing app");
            Reject(() => new CompanionInstallationService(() => false).Install(game, source), "Directory at file destination accepted");
            Check(File.ReadAllText(Path.Combine(destination, "ADTCompanion.lua")) == "keep existing app", "An earlier file changed before full validation");
        });
    }

    private static (string Game, string Source) Fixture(string root, string name)
    {
        var fixture = Path.Combine(root, name);
        var game = Path.Combine(fixture, "assetto-corsa");
        var source = Path.Combine(fixture, "payload");
        Directory.CreateDirectory(Path.Combine(game, "content", "cars"));
        File.WriteAllText(Path.Combine(game, "acs.exe"), "isolated test marker; not executable");
        Directory.CreateDirectory(source);
        foreach (var file in CompanionInstallationService.FileNames) File.WriteAllText(Path.Combine(source, file), "fixture " + file);
        return (game, source);
    }

    private static void Reject(Action action, string failure)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { return; }
        throw new Exception(failure);
    }
}
