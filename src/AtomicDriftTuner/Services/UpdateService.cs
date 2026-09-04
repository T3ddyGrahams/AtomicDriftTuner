using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class UpdateService
{
    public const string RepositoryOwner = "T3ddyGrahams";
    public const string RepositoryName = "AtomicDriftTuner";
    public const string ReleasesPageUrl = "https://github.com/T3ddyGrahams/AtomicDriftTuner/releases";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"AtomicDriftTuner/{DistributionInfo.Version}");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public async Task<AtomicUpdateCheckResult> CheckAsync(
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases?per_page=30";

        using var response = await Http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var rateRemaining =
                response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
                    ? remaining.FirstOrDefault()
                    : null;

            var suffix =
                string.IsNullOrWhiteSpace(rateRemaining)
                    ? ""
                    : $" GitHub rate-limit remaining: {rateRemaining}.";

            throw new InvalidOperationException(
                $"GitHub update check failed: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{suffix}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var releases =
            await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(
                stream,
                cancellationToken: cancellationToken) ?? [];

        var candidates =
            releases
                .Where(x => !x.Draft && (includePrerelease || !x.Prerelease))
                .Select(ToReleaseInfo)
                .ToList();

        if (candidates.Count == 0)
        {
            return new AtomicUpdateCheckResult
            {
                CurrentVersion = DistributionInfo.Version,
                Message = includePrerelease
                    ? "No published Atomic Drift Tuner releases were found on GitHub."
                    : "No stable (non-prerelease) Atomic Drift Tuner releases were found on GitHub."
            };
        }

        var latest = candidates[0];
        foreach (var candidate in candidates.Skip(1))
        {
            if (CompareVersionStrings(candidate.TagName, latest.TagName) > 0)
                latest = candidate;
        }

        int comparison = CompareVersionStrings(latest.TagName, DistributionInfo.Version);

        return new AtomicUpdateCheckResult
        {
            CurrentVersion = DistributionInfo.Version,
            LatestRelease = latest,
            UpdateAvailable = comparison > 0,
            CurrentBuildIsNewer = comparison < 0,
            Message = comparison switch
            {
                > 0 => $"Update available: {latest.TagName}",
                < 0 => $"This development build ({DistributionInfo.Version}) is newer than the latest matching published release ({latest.TagName}).",
                _ => $"You are up to date: {latest.TagName}"
            }
        };
    }

    public async Task<AtomicDownloadResult> DownloadAsync(
        AtomicReleaseAsset asset,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateAsset(asset);

        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Choose a valid download location.");

        Directory.CreateDirectory(directory);
        var partial = destination + ".part";

        try
        {
            if (File.Exists(partial))
                File.Delete(partial);

            using var response = await Http.GetAsync(
                asset.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var length = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[1024 * 128];
            long total = 0;

            await using (var output = new FileStream(
                partial,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 128,
                useAsync: true))
            {
                while (true)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read <= 0)
                        break;

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;

                    if (length is > 0)
                        progress?.Report(Math.Clamp(total * 100d / length.Value, 0, 100));
                }

                await output.FlushAsync(cancellationToken);
            }

            if (length is > 0 && total != length.Value)
                throw new InvalidOperationException(
                    $"The update download was incomplete. Expected {length.Value} bytes but received {total}.");

            if (asset.SizeBytes > 0 && total != asset.SizeBytes)
                throw new InvalidOperationException(
                    $"The downloaded file size did not match the GitHub release asset. Expected {asset.SizeBytes} bytes but received {total}.");

            if (File.Exists(destination))
                File.Delete(destination);
            File.Move(partial, destination);

            var hash = await ComputeSha256Async(destination, cancellationToken);
            progress?.Report(100);

            return new AtomicDownloadResult
            {
                FilePath = destination,
                Sha256 = hash,
                SizeBytes = new FileInfo(destination).Length
            };
        }
        catch
        {
            try
            {
                if (File.Exists(partial))
                    File.Delete(partial);
            }
            catch
            {
                // Cleanup failure must not hide the original download error.
            }

            throw;
        }
    }

    public static AtomicReleaseAsset? FindInstaller(AtomicReleaseInfo release) =>
        release.Assets.FirstOrDefault(
            x => x.Name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase));

    public static AtomicReleaseAsset? FindPortable(AtomicReleaseInfo release) =>
        release.Assets.FirstOrDefault(
            x => x.Name.EndsWith("-portable.zip", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            useAsync: true);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ValidateAsset(AtomicReleaseAsset asset)
    {
        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Atomic refused a non-HTTPS update download URL.");
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(
                $"/{RepositoryOwner}/{RepositoryName}/releases/download/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Atomic refused an update asset that did not come from the official AtomicDriftTuner GitHub Releases path.");
        }
    }

    private static AtomicReleaseInfo ToReleaseInfo(GitHubReleaseDto dto) =>
        new()
        {
            TagName = dto.TagName ?? "",
            Name = string.IsNullOrWhiteSpace(dto.Name) ? dto.TagName ?? "" : dto.Name,
            Body = dto.Body ?? "",
            HtmlUrl = dto.HtmlUrl ?? ReleasesPageUrl,
            Prerelease = dto.Prerelease,
            PublishedAt = dto.PublishedAt,
            Assets = dto.Assets
                .Select(
                    x => new AtomicReleaseAsset
                    {
                        Name = x.Name ?? "",
                        DownloadUrl = x.BrowserDownloadUrl ?? "",
                        ContentType = x.ContentType ?? "",
                        SizeBytes = x.Size
                    })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.DownloadUrl))
                .ToList()
        };

    internal static int CompareVersionStrings(string left, string right)
    {
        bool leftOk = AtomicVersion.TryParse(left, out var leftVersion);
        bool rightOk = AtomicVersion.TryParse(right, out var rightVersion);

        if (leftOk && rightOk)
            return leftVersion.CompareTo(rightVersion);

        return string.Compare(left.TrimStart('v', 'V'), right.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto> Assets { get; set; } = [];
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    private readonly record struct AtomicVersion(
        int Major,
        int Minor,
        int Patch,
        string[] Prerelease) : IComparable<AtomicVersion>
    {
        public static bool TryParse(string value, out AtomicVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var clean = value.Trim().TrimStart('v', 'V');
            int plus = clean.IndexOf('+');
            if (plus >= 0)
                clean = clean[..plus];

            string numeric = clean;
            string prerelease = "";
            int dash = clean.IndexOf('-');
            if (dash >= 0)
            {
                numeric = clean[..dash];
                prerelease = clean[(dash + 1)..];
            }

            var parts = numeric.Split('.');
            if (parts.Length < 3 ||
                !int.TryParse(parts[0], out int major) ||
                !int.TryParse(parts[1], out int minor) ||
                !int.TryParse(parts[2], out int patch))
            {
                return false;
            }

            version = new AtomicVersion(
                major,
                minor,
                patch,
                string.IsNullOrWhiteSpace(prerelease)
                    ? []
                    : prerelease.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries));
            return true;
        }

        public int CompareTo(AtomicVersion other)
        {
            int compare = Major.CompareTo(other.Major);
            if (compare != 0) return compare;
            compare = Minor.CompareTo(other.Minor);
            if (compare != 0) return compare;
            compare = Patch.CompareTo(other.Patch);
            if (compare != 0) return compare;

            if (Prerelease.Length == 0 && other.Prerelease.Length == 0) return 0;
            if (Prerelease.Length == 0) return 1;
            if (other.Prerelease.Length == 0) return -1;

            int count = Math.Max(Prerelease.Length, other.Prerelease.Length);
            for (int i = 0; i < count; i++)
            {
                if (i >= Prerelease.Length) return -1;
                if (i >= other.Prerelease.Length) return 1;

                string a = Prerelease[i];
                string b = other.Prerelease[i];
                bool aNumber = int.TryParse(a, out int aInt);
                bool bNumber = int.TryParse(b, out int bInt);

                if (aNumber && bNumber)
                {
                    compare = aInt.CompareTo(bInt);
                }
                else if (aNumber != bNumber)
                {
                    compare = aNumber ? -1 : 1;
                }
                else
                {
                    compare = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                }

                if (compare != 0) return compare;
            }

            return 0;
        }
    }
}
