namespace AtomicDriftTuner.Models;

public sealed class AtomicReleaseAsset
{
    public string Name { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
}

public sealed class AtomicReleaseInfo
{
    public string TagName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Body { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public bool Prerelease { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public List<AtomicReleaseAsset> Assets { get; set; } = [];
}

public sealed class AtomicUpdateCheckResult
{
    public string CurrentVersion { get; set; } = "";
    public AtomicReleaseInfo? LatestRelease { get; set; }
    public bool UpdateAvailable { get; set; }
    public bool CurrentBuildIsNewer { get; set; }
    public string Message { get; set; } = "";
}

public sealed class AtomicDownloadResult
{
    public string FilePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
}
