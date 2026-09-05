namespace AtomicDriftTuner.Models;

public sealed class AtomicReleaseAsset
{
    private string _name =
        string.Empty;

    private string _downloadUrl =
        string.Empty;

    private string _contentType =
        string.Empty;

    public string Name
    {
        get => _name;

        set =>
            _name =
                value?.Trim() ??
                string.Empty;
    }

    public string DownloadUrl
    {
        get => _downloadUrl;

        set =>
            _downloadUrl =
                value?.Trim() ??
                string.Empty;
    }

    public string ContentType
    {
        get => _contentType;

        set =>
            _contentType =
                value?.Trim() ??
                string.Empty;
    }

    public long SizeBytes { get; set; }
}

public sealed class AtomicReleaseInfo
{
    private string _tagName =
        string.Empty;

    private string _name =
        string.Empty;

    private string _body =
        string.Empty;

    private string _htmlUrl =
        string.Empty;

    private List<AtomicReleaseAsset> _assets =
        [];

    public string TagName
    {
        get => _tagName;

        set =>
            _tagName =
                value?.Trim() ??
                string.Empty;
    }

    public string Name
    {
        get => _name;

        set =>
            _name =
                value?.Trim() ??
                string.Empty;
    }

    public string Body
    {
        get => _body;

        set =>
            _body =
                value ??
                string.Empty;
    }

    public string HtmlUrl
    {
        get => _htmlUrl;

        set =>
            _htmlUrl =
                value?.Trim() ??
                string.Empty;
    }

    public bool Prerelease { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public List<AtomicReleaseAsset> Assets
    {
        get => _assets;

        set =>
            _assets =
                value ??
                [];
    }
}

public sealed class AtomicUpdateCheckResult
{
    private string _currentVersion =
        string.Empty;

    private string _message =
        string.Empty;

    public string CurrentVersion
    {
        get => _currentVersion;

        set =>
            _currentVersion =
                value?.Trim() ??
                string.Empty;
    }

    public AtomicReleaseInfo? LatestRelease { get; set; }

    public bool UpdateAvailable { get; set; }

    public bool CurrentBuildIsNewer { get; set; }

    public string Message
    {
        get => _message;

        set =>
            _message =
                value ??
                string.Empty;
    }
}

public sealed class AtomicDownloadResult
{
    private string _filePath =
        string.Empty;

    private string _sha256 =
        string.Empty;

    public string FilePath
    {
        get => _filePath;

        set =>
            _filePath =
                value?.Trim() ??
                string.Empty;
    }

    public string Sha256
    {
        get => _sha256;

        set =>
            _sha256 =
                value?.Trim() ??
                string.Empty;
    }

    public long SizeBytes { get; set; }
}