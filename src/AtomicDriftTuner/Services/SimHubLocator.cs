using System.Diagnostics;
using Microsoft.Win32;

namespace AtomicDriftTuner.Services;

public static class SimHubLocator
{
    public static string? FindSimHubExe(string? saved = null)
    {
        var savedExe = NormalizeSaved(saved);
        if (savedExe is not null)
            return savedExe;

        // If SimHub is running, its own process location is the most reliable answer.
        try
        {
            foreach (var process in Process.GetProcessesByName("SimHubWPF"))
            {
                try
                {
                    var file = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                        return file;
                }
                catch
                {
                    // Process inspection can be denied across privilege boundaries.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch { }

        foreach (var root in RegistryInstallRoots())
        {
            var exe = Path.Combine(root, "SimHubWPF.exe");
            if (File.Exists(exe))
                return exe;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SimHub", "SimHubWPF.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SimHub", "SimHubWPF.exe"),
            @"C:\Program Files (x86)\SimHub\SimHubWPF.exe",
            @"C:\Program Files\SimHub\SimHubWPF.exe"
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? FindSimHubRoot(string? saved = null)
    {
        var exe = FindSimHubExe(saved);
        return string.IsNullOrWhiteSpace(exe) ? null : Path.GetDirectoryName(exe);
    }

    public static bool IsValidRoot(string? root) =>
        !string.IsNullOrWhiteSpace(root) &&
        File.Exists(Path.Combine(root, "SimHubWPF.exe")) &&
        File.Exists(Path.Combine(root, "SimHub.Plugins.dll"));

    private static string? NormalizeSaved(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
            return null;

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(saved.Trim().Trim('"'));

            if (File.Exists(expanded) &&
                Path.GetFileName(expanded).Equals("SimHubWPF.exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(expanded);

            if (Directory.Exists(expanded))
            {
                var exe = Path.Combine(expanded, "SimHubWPF.exe");
                if (File.Exists(exe))
                    return Path.GetFullPath(exe);
            }
        }
        catch { }

        return null;
    }

    private static IReadOnlyList<string> RegistryInstallRoots()
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall =
                        baseKey.OpenSubKey(
                            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

                    if (uninstall is null)
                        continue;

                    foreach (var subName in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var sub = uninstall.OpenSubKey(subName);
                            var display = sub?.GetValue("DisplayName")?.ToString() ?? "";

                            if (!display.Contains("SimHub", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var location = sub?.GetValue("InstallLocation")?.ToString();

                            if (!string.IsNullOrWhiteSpace(location) &&
                                Directory.Exists(location) &&
                                seen.Add(location))
                            {
                                results.Add(location);
                            }
                        }
                        catch
                        {
                            // Ignore individual malformed/inaccessible uninstall records.
                        }
                    }
                }
                catch
                {
                    // Registry view/hive may be unavailable under restricted accounts.
                }
            }
        }

        return results;
    }

}