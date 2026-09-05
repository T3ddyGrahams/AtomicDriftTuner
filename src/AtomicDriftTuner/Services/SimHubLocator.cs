using System.Diagnostics;
using Microsoft.Win32;

namespace AtomicDriftTuner.Services;

public static class SimHubLocator
{
    private const string SimHubExecutableName =
        "SimHubWPF.exe";

    private const string SimHubPluginsAssemblyName =
        "SimHub.Plugins.dll";

    public static string? FindSimHubExe(
        string? saved = null)
    {
        var savedExe =
            NormalizeSaved(
                saved);

        if (savedExe is not null)
        {
            return savedExe;
        }

        // If SimHub is currently running, its process image is normally the
        // strongest available indication of the installation being used.
        var runningExe =
            TryFindRunningSimHub();

        if (runningExe is not null)
        {
            return runningExe;
        }

        foreach (var root in
                 RegistryInstallRoots())
        {
            var exe =
                ResolveExecutableFromRoot(
                    root);

            if (exe is not null)
            {
                return exe;
            }
        }

        foreach (var candidate in
                 CommonExecutableCandidates())
        {
            var exe =
                NormalizeExecutableCandidate(
                    candidate);

            if (exe is not null)
            {
                return exe;
            }
        }

        return null;
    }

    public static string? FindSimHubRoot(
        string? saved = null)
    {
        var exe =
            FindSimHubExe(
                saved);

        if (exe is null)
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(
                exe);
        }
        catch (
            Exception ex)
            when (IsPathException(
                ex))
        {
            return null;
        }
    }

    public static bool IsValidRoot(
        string? root)
    {
        if (string.IsNullOrWhiteSpace(
                root))
        {
            return false;
        }

        string normalized;

        try
        {
            normalized =
                NormalizePath(
                    root);
        }
        catch (
            Exception ex)
            when (IsPathException(
                ex))
        {
            return false;
        }

        var executable =
            Path.Combine(
                normalized,
                SimHubExecutableName);

        var pluginsAssembly =
            Path.Combine(
                normalized,
                SimHubPluginsAssemblyName);

        return
            IsExpectedExecutable(
                executable) &&
            File.Exists(
                pluginsAssembly);
    }

    private static string? NormalizeSaved(
        string? saved)
    {
        if (string.IsNullOrWhiteSpace(
                saved))
        {
            return null;
        }

        string expanded;

        try
        {
            expanded =
                NormalizePath(
                    saved);
        }
        catch (
            Exception ex)
            when (IsPathException(
                ex))
        {
            return null;
        }

        if (File.Exists(
                expanded))
        {
            return NormalizeExecutableCandidate(
                expanded);
        }

        if (Directory.Exists(
                expanded))
        {
            return ResolveExecutableFromRoot(
                expanded);
        }

        return null;
    }

    private static string? TryFindRunningSimHub()
    {
        Process[] processes;

        try
        {
            processes =
                Process.GetProcessesByName(
                    "SimHubWPF");
        }
        catch (
            Exception ex)
            when (
                ex is InvalidOperationException ||
                ex is System.ComponentModel.Win32Exception)
        {
            return null;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var path =
                        process.MainModule
                            ?.FileName;

                    var normalized =
                        NormalizeExecutableCandidate(
                            path);

                    if (normalized is not null)
                    {
                        return normalized;
                    }
                }
                catch (
                    Exception ex)
                    when (
                        ex is InvalidOperationException ||
                        ex is NotSupportedException ||
                        ex is System.ComponentModel.Win32Exception)
                {
                    // Process inspection can fail when SimHub is running at a
                    // different privilege level. Continue with other discovery
                    // methods rather than treating that as an ADT failure.
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                    // Process-handle cleanup is best effort.
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> RegistryInstallRoots()
    {
        var results =
            new List<string>();

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var hives =
            new[]
            {
                RegistryHive.CurrentUser,
                RegistryHive.LocalMachine
            };

        var views =
            new[]
            {
                RegistryView.Registry32,
                RegistryView.Registry64
            };

        foreach (var hive in hives)
        {
            foreach (var view in views)
            {
                ReadRegistryInstallRoots(
                    hive,
                    view,
                    results,
                    seen);
            }
        }

        return results;
    }

    private static void ReadRegistryInstallRoots(
        RegistryHive hive,
        RegistryView view,
        List<string> results,
        HashSet<string> seen)
    {
        try
        {
            using var baseKey =
                RegistryKey.OpenBaseKey(
                    hive,
                    view);

            using var uninstall =
                baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

            if (uninstall is null)
            {
                return;
            }

            foreach (var subName in
                     uninstall.GetSubKeyNames())
            {
                try
                {
                    using var sub =
                        uninstall.OpenSubKey(
                            subName);

                    if (sub is null)
                    {
                        continue;
                    }

                    var displayName =
                        sub.GetValue(
                                "DisplayName")
                            ?.ToString();

                    if (
                        string.IsNullOrWhiteSpace(
                            displayName) ||
                        !displayName.Contains(
                            "SimHub",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var location =
                        sub.GetValue(
                                "InstallLocation")
                            ?.ToString();

                    var normalized =
                        NormalizeRegistryRoot(
                            location);

                    if (
                        normalized is not null &&
                        seen.Add(
                            normalized))
                    {
                        results.Add(
                            normalized);
                    }
                }
                catch (
                    Exception ex)
                    when (IsRegistryReadException(
                        ex))
                {
                    // Ignore one malformed or inaccessible uninstall entry.
                }
            }
        }
        catch (
            Exception ex)
            when (IsRegistryReadException(
                ex))
        {
            // Some hives/views may be inaccessible under restricted accounts.
        }
    }

    private static string? NormalizeRegistryRoot(
        string? location)
    {
        if (string.IsNullOrWhiteSpace(
                location))
        {
            return null;
        }

        try
        {
            var normalized =
                NormalizePath(
                    location);

            return Directory.Exists(
                    normalized)
                ? normalized
                : null;
        }
        catch (
            Exception ex)
            when (IsPathException(
                ex))
        {
            return null;
        }
    }

    private static string? ResolveExecutableFromRoot(
        string root)
    {
        if (string.IsNullOrWhiteSpace(
                root))
        {
            return null;
        }

        string normalizedRoot;

        try
        {
            normalizedRoot =
                NormalizePath(
                    root);
        }
        catch (
            Exception ex)
            when (IsPathException(
                ex))
        {
            return null;
        }

        if (!Directory.Exists(
                normalizedRoot))
        {
            return null;
        }

        return NormalizeExecutableCandidate(
            Path.Combine(
                normalizedRoot,
                SimHubExecutableName));
    }

    private static string? NormalizeExecutableCandidate(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return null;
        }

        string full;

        try
        {
            full =
                NormalizePath(
                    path);
        }
        catch (
            Exception ex)
            when (IsPathException(
                ex))
        {
            return null;
        }

        return IsExpectedExecutable(
                full)
            ? full
            : null;
    }

    private static bool IsExpectedExecutable(
        string path)
    {
        try
        {
            return
                File.Exists(
                    path) &&
                string.Equals(
                    Path.GetFileName(
                        path),
                    SimHubExecutableName,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (
            Exception ex)
            when (IsPathException(
                ex))
        {
            return false;
        }
    }

    private static IEnumerable<string> CommonExecutableCandidates()
    {
        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var roots =
            new[]
            {
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86),

                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),

                @"C:\Program Files (x86)",

                @"C:\Program Files"
            };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(
                    root))
            {
                continue;
            }

            string candidate;

            try
            {
                candidate =
                    Path.Combine(
                        root,
                        "SimHub",
                        SimHubExecutableName);
            }
            catch (
                Exception ex)
                when (IsPathException(
                    ex))
            {
                continue;
            }

            if (seen.Add(
                    candidate))
            {
                yield return candidate;
            }
        }
    }

    private static string NormalizePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            throw new ArgumentException(
                "Path is required.",
                nameof(path));
        }

        var expanded =
            Environment
                .ExpandEnvironmentVariables(
                    path
                        .Trim()
                        .Trim('"'));

        if (string.IsNullOrWhiteSpace(
                expanded))
        {
            throw new ArgumentException(
                "Path is empty after normalization.",
                nameof(path));
        }

        return Path.GetFullPath(
            expanded);
    }

    private static bool IsRegistryReadException(
        Exception exception)
    {
        return exception is
            UnauthorizedAccessException or
            IOException or
            System.Security.SecurityException or
            ArgumentException;
    }

    private static bool IsPathException(
        Exception exception)
    {
        return exception is
            ArgumentException or
            NotSupportedException or
            PathTooLongException or
            IOException or
            UnauthorizedAccessException;
    }
}
