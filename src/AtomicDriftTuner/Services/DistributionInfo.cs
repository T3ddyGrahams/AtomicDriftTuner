using System.Reflection;

namespace AtomicDriftTuner.Services;

public static class DistributionInfo
{
    public static string Version { get; } = GetVersion();

    public static string DisplayVersion { get; } = CreateDisplayVersion(Version);

    public const string Channel = "Public Beta";

    public const string SupportSchema = "atomic-drift-tuner/support/v1";

    private static string GetVersion()
    {
        var assembly = typeof(DistributionInfo).Assembly;

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparator = informationalVersion.IndexOf('+');

            if (metadataSeparator >= 0)
            {
                informationalVersion = informationalVersion[..metadataSeparator];
            }

            return informationalVersion;
        }

        var assemblyVersion = assembly.GetName().Version;

        return assemblyVersion?.ToString() ?? "unknown";
    }

    private static string CreateDisplayVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "Unknown Version";
        }

        if (version.Contains("-beta.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = version.Split(
                "-beta.",
                2,
                StringSplitOptions.None);

            return $"v{parts[0]} BETA {parts[1]}";
        }

        if (version.EndsWith("-beta", StringComparison.OrdinalIgnoreCase))
        {
            return $"v{version[..^5]} BETA";
        }

        return $"v{version}";
    }
}
