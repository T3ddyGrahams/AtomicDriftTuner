using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class UpdateService
{
    public const string RepositoryOwner =
        "T3ddyGrahams";

    public const string RepositoryName =
        "AtomicDriftTuner";

    public const string ReleasesPageUrl =
        "https://github.com/T3ddyGrahams/AtomicDriftTuner/releases";

    private const string GitHubApiVersion =
        "2026-03-10";

    private const int MaximumReleaseCount =
        100;

    private const long MaximumApiResponseBytes =
        8L * 1024 * 1024;

    // ADT packages are expected to be far smaller than this. The cap exists
    // to prevent a malformed or unexpected response from filling a drive.
    private const long MaximumDownloadBytes =
        512L * 1024 * 1024;

    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(
            30);

    private static readonly TimeSpan DownloadIdleTimeout =
        TimeSpan.FromSeconds(
            30);

    private static readonly HttpClient ApiHttp =
        CreateApiHttpClient();

    private static readonly HttpClient DownloadHttp =
        CreateDownloadHttpClient();

    // Keep the authoritative metadata returned by the official GitHub API.
    // DownloadAsync only accepts assets that this UpdateService instance has
    // actually discovered from that API, so a caller cannot fabricate an
    // official-looking github.com release URL and bypass release metadata.
    private readonly ConcurrentDictionary<string, DiscoveredAssetMetadata>
        _discoveredAssetsByDownloadUrl =
            new(
                StringComparer.Ordinal);

    private static HttpClient CreateApiHttpClient()
    {
        var handler =
            new HttpClientHandler
            {
                AllowAutoRedirect =
                    false,

                UseCookies =
                    false,

                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate |
                    DecompressionMethods.Brotli
            };

        var client =
            new HttpClient(
                handler,
                disposeHandler: true)
            {
                Timeout =
                    RequestTimeout,

                MaxResponseContentBufferSize =
                    MaximumApiResponseBytes
            };

        AddUserAgent(
            client);

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));

        client.DefaultRequestHeaders.Add(
            "X-GitHub-Api-Version",
            GitHubApiVersion);

        return client;
    }

    private static HttpClient CreateDownloadHttpClient()
    {
        var handler =
            new HttpClientHandler
            {
                AllowAutoRedirect =
                    true,

                MaxAutomaticRedirections =
                    5,

                UseCookies =
                    false,

                AutomaticDecompression =
                    DecompressionMethods.None
            };

        var client =
            new HttpClient(
                handler,
                disposeHandler: true)
            {
                // With ResponseHeadersRead this covers connection/header setup.
                // Stream reads also receive an independent idle timeout below.
                Timeout =
                    RequestTimeout
            };

        AddUserAgent(
            client);

        return client;
    }

    private static void AddUserAgent(
        HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"AtomicDriftTuner/{DistributionInfo.Version}");
    }

    public async Task<AtomicUpdateCheckResult> CheckAsync(
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (
            !AtomicVersion.TryParse(
                DistributionInfo.Version,
                out var currentVersion))
        {
            throw new InvalidOperationException(
                $"ADT cannot safely compare updates because the installed version '{DistributionInfo.Version}' is not a valid ADT semantic version.");
        }

        var url =
            $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases?per_page={MaximumReleaseCount}";

        using var response =
            await ApiHttp.GetAsync(
                url,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var rateRemaining =
                response.Headers.TryGetValues(
                    "X-RateLimit-Remaining",
                    out var remaining)
                    ? remaining.FirstOrDefault()
                    : null;

            var suffix =
                string.IsNullOrWhiteSpace(
                    rateRemaining)
                    ? string.Empty
                    : $" GitHub rate-limit remaining: {rateRemaining}.";

            throw new InvalidOperationException(
                $"GitHub update check failed: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{suffix}");
        }

        ValidateApiResponseOrigin(
            response);

        var apiLength =
            response.Content.Headers.ContentLength;

        if (
            apiLength is > MaximumApiResponseBytes)
        {
            throw new InvalidDataException(
                "ADT refused an unexpectedly large GitHub release response.");
        }

        List<GitHubReleaseDto> releases;

        try
        {
            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            releases =
                await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(
                    stream,
                    cancellationToken:
                        cancellationToken)
                ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "GitHub returned release metadata that ADT could not parse safely.",
                ex);
        }

        var candidates =
            new List<ReleaseCandidate>();

        foreach (var dto in releases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (
                dto is null ||
                dto.Draft ||
                !AtomicVersion.TryParse(
                    dto.TagName ?? string.Empty,
                    out var parsedVersion))
            {
                continue;
            }

            var effectivePrerelease =
                dto.Prerelease ||
                parsedVersion.IsPrerelease;

            if (
                !includePrerelease &&
                effectivePrerelease)
            {
                continue;
            }

            var release =
                ToReleaseInfo(
                    dto,
                    effectivePrerelease,
                    parsedVersion);

            candidates.Add(
                new ReleaseCandidate(
                    release,
                    parsedVersion));
        }

        if (candidates.Count == 0)
        {
            return new AtomicUpdateCheckResult
            {
                CurrentVersion =
                    DistributionInfo.Version,

                Message =
                    includePrerelease
                        ? "No published ADT releases with valid semantic-version tags were found on GitHub."
                        : "No stable ADT releases with valid semantic-version tags were found on GitHub."
            };
        }

        var latest =
            candidates[0];

        foreach (
            var candidate in
            candidates.Skip(
                1))
        {
            if (
                candidate.Version.CompareTo(
                    latest.Version) >
                0)
            {
                latest =
                    candidate;
            }
        }

        var comparison =
            latest.Version.CompareTo(
                currentVersion);

        return new AtomicUpdateCheckResult
        {
            CurrentVersion =
                DistributionInfo.Version,

            LatestRelease =
                latest.Release,

            UpdateAvailable =
                comparison >
                0,

            CurrentBuildIsNewer =
                comparison <
                0,

            Message =
                comparison switch
                {
                    > 0 =>
                        $"Update available: {latest.Release.TagName}",

                    < 0 =>
                        $"This development build ({DistributionInfo.Version}) is newer than the latest matching published release ({latest.Release.TagName}).",

                    _ =>
                        $"You are up to date: {latest.Release.TagName}"
                }
        };
    }

    public async Task<AtomicDownloadResult> DownloadAsync(
        AtomicReleaseAsset asset,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            asset);

        cancellationToken.ThrowIfCancellationRequested();

        var kind =
            ValidateAsset(
                asset);

        var discovered =
            ValidateDiscoveredAsset(
                asset);

        if (
            discovered.Version.CompareTo(
                GetInstalledVersion()) <
            0)
        {
            throw new InvalidOperationException(
                $"ADT refused to download older release {discovered.ReleaseTag} over installed {DistributionInfo.Version}. Open the GitHub Releases page manually if you intentionally want to downgrade.");
        }

        var destination =
            NormalizeDestinationPath(
                destinationPath,
                kind);

        var directory =
            Path.GetDirectoryName(
                destination)
            ?? throw new InvalidOperationException(
                "Choose a valid download location.");

        Directory.CreateDirectory(
            directory);

        var partial =
            Path.Combine(
                directory,
                $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.part");

        try
        {
            using var response =
                await DownloadHttp.GetAsync(
                    asset.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"GitHub update download failed: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            ValidateFinalDownloadOrigin(
                response);

            var length =
                response.Content.Headers.ContentLength;

            ValidateDeclaredSizes(
                asset,
                length);

            await using var input =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            var buffer =
                new byte[1024 * 128];

            long total =
                0;

            await using (
                var output =
                    new FileStream(
                        partial,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 128,
                        useAsync: true))
            {
                while (true)
                {
                    var read =
                        await ReadWithIdleTimeoutAsync(
                            input,
                            buffer.AsMemory(
                                0,
                                buffer.Length),
                            cancellationToken);

                    if (read <= 0)
                    {
                        break;
                    }

                    total +=
                        read;

                    if (
                        total >
                        MaximumDownloadBytes)
                    {
                        throw new InvalidDataException(
                            $"ADT stopped the update download because it exceeded the {FormatByteLimit(MaximumDownloadBytes)} safety limit.");
                    }

                    await output.WriteAsync(
                        buffer.AsMemory(
                            0,
                            read),
                        cancellationToken);

                    if (
                        length is >
                        0)
                    {
                        SafeReportProgress(
                            progress,
                            Math.Clamp(
                                total * 100d /
                                length.Value,
                                0,
                                100));
                    }
                }

                await output.FlushAsync(
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (
                length is >
                    0 &&
                total !=
                    length.Value)
            {
                throw new InvalidDataException(
                    $"The update download was incomplete. Expected {length.Value} bytes but received {total}.");
            }

            if (
                asset.SizeBytes >
                    0 &&
                total !=
                    asset.SizeBytes)
            {
                throw new InvalidDataException(
                    $"The downloaded file size did not match the GitHub release asset. Expected {asset.SizeBytes} bytes but received {total}.");
            }

            await ValidateDownloadedFileFormatAsync(
                partial,
                kind,
                cancellationToken);

            var hash =
                await ComputeSha256Async(
                    partial,
                    cancellationToken);

            if (
                !string.IsNullOrWhiteSpace(
                    discovered.ExpectedSha256) &&
                !string.Equals(
                    hash,
                    discovered.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "ADT discarded the update because its SHA-256 did not match the digest reported by GitHub for this release asset.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Publish only after the entire download has passed size, format
            // and hash/digest checks. The temporary file lives beside the final
            // destination so the final move remains on the same volume.
            File.Move(
                partial,
                destination,
                overwrite: true);

            SafeReportProgress(
                progress,
                100);

            return new AtomicDownloadResult
            {
                FilePath =
                    destination,

                Sha256 =
                    hash,

                SizeBytes =
                    total
            };
        }
        catch
        {
            TryDeleteFile(
                partial);

            throw;
        }
    }

    public static AtomicReleaseAsset? FindInstaller(
        AtomicReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(
            release);

        return release.Assets.FirstOrDefault(
            asset =>
                IsSelectableAsset(
                    asset,
                    ReleaseAssetKind.Installer));
    }

    public static AtomicReleaseAsset? FindPortable(
        AtomicReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(
            release);

        return release.Assets.FirstOrDefault(
            asset =>
                IsSelectableAsset(
                    asset,
                    ReleaseAssetKind.PortableZip));
    }

    private AtomicReleaseInfo ToReleaseInfo(
        GitHubReleaseDto dto,
        bool effectivePrerelease,
        AtomicVersion parsedVersion)
    {
        var assets =
            new List<AtomicReleaseAsset>();

        foreach (
            var dtoAsset in
            dto.Assets)
        {
            if (
                dtoAsset is null ||
                string.IsNullOrWhiteSpace(
                    dtoAsset.Name) ||
                string.IsNullOrWhiteSpace(
                    dtoAsset.BrowserDownloadUrl))
            {
                continue;
            }

            var asset =
                new AtomicReleaseAsset
                {
                    Name =
                        dtoAsset.Name,

                    DownloadUrl =
                        dtoAsset.BrowserDownloadUrl,

                    ContentType =
                        dtoAsset.ContentType ??
                        string.Empty,

                    SizeBytes =
                        dtoAsset.Size
                };

            // Do not expose a release asset to the update UI unless its initial
            // URL is the official repository's GitHub release-download path.
            if (!IsOfficialReleaseAssetUrl(
                    asset,
                    requireKnownUpdateKind: false))
            {
                continue;
            }

            assets.Add(
                asset);

            _ =
                TryNormalizeSha256Digest(
                    dtoAsset.Digest,
                    out var sha256);

            _discoveredAssetsByDownloadUrl[
                asset.DownloadUrl] =
                new DiscoveredAssetMetadata(
                    asset.Name,
                    asset.SizeBytes,
                    dto.TagName ??
                        string.Empty,
                    parsedVersion,
                    string.IsNullOrWhiteSpace(
                        sha256)
                        ? null
                        : sha256);
        }

        return new AtomicReleaseInfo
        {
            TagName =
                dto.TagName ??
                string.Empty,

            Name =
                string.IsNullOrWhiteSpace(
                    dto.Name)
                    ? dto.TagName ??
                      string.Empty
                    : dto.Name,

            Body =
                dto.Body ??
                string.Empty,

            HtmlUrl =
                NormalizeOfficialReleaseHtmlUrl(
                    dto.HtmlUrl),

            Prerelease =
                effectivePrerelease,

            PublishedAt =
                dto.PublishedAt,

            Assets =
                assets
        };
    }

    private DiscoveredAssetMetadata ValidateDiscoveredAsset(
        AtomicReleaseAsset asset)
    {
        if (
            !_discoveredAssetsByDownloadUrl.TryGetValue(
                asset.DownloadUrl,
                out var metadata))
        {
            throw new InvalidOperationException(
                "ADT refused an update asset that was not discovered from the official GitHub Releases API during this update session.");
        }

        if (
            !string.Equals(
                asset.Name,
                metadata.Name,
                StringComparison.Ordinal) ||
            asset.SizeBytes !=
                metadata.SizeBytes)
        {
            throw new InvalidDataException(
                "ADT refused an update asset whose filename or size no longer matched the GitHub metadata ADT originally loaded.");
        }

        return metadata;
    }

    private static AtomicVersion GetInstalledVersion()
    {
        if (
            !AtomicVersion.TryParse(
                DistributionInfo.Version,
                out var currentVersion))
        {
            throw new InvalidOperationException(
                $"ADT cannot safely validate the update because installed version '{DistributionInfo.Version}' is not a valid ADT semantic version.");
        }

        return currentVersion;
    }

    private static ReleaseAssetKind ValidateAsset(
        AtomicReleaseAsset asset)
    {
        if (
            !TryGetAssetKind(
                asset.Name,
                out var kind))
        {
            throw new InvalidOperationException(
                "ADT only downloads its recognized installer (-setup.exe) or portable package (-portable.zip) from the Updates workspace.");
        }

        if (
            asset.SizeBytes <=
            0)
        {
            throw new InvalidOperationException(
                "ADT refused an update asset with an unknown or empty GitHub size.");
        }

        if (
            asset.SizeBytes >
            MaximumDownloadBytes)
        {
            throw new InvalidOperationException(
                $"ADT refused an update asset larger than the {FormatByteLimit(MaximumDownloadBytes)} safety limit.");
        }

        if (!IsOfficialReleaseAssetUrl(
                asset,
                requireKnownUpdateKind: true))
        {
            throw new InvalidOperationException(
                "ADT refused an update asset that did not match the official repository's GitHub Releases path and filename.");
        }

        return kind;
    }

    private static bool IsSelectableAsset(
        AtomicReleaseAsset? asset,
        ReleaseAssetKind expectedKind)
    {
        if (asset is null)
        {
            return false;
        }

        return
            TryGetAssetKind(
                asset.Name,
                out var actualKind) &&
            actualKind ==
                expectedKind &&
            asset.SizeBytes >
                0 &&
            asset.SizeBytes <=
                MaximumDownloadBytes &&
            IsOfficialReleaseAssetUrl(
                asset,
                requireKnownUpdateKind: true);
    }

    private static bool IsOfficialReleaseAssetUrl(
        AtomicReleaseAsset asset,
        bool requireKnownUpdateKind)
    {
        if (
            string.IsNullOrWhiteSpace(
                asset.Name) ||
            !IsSafeAssetFileName(
                asset.Name) ||
            (
                requireKnownUpdateKind &&
                !TryGetAssetKind(
                    asset.Name,
                    out _)
            ) ||
            !TryCreateOfficialReleaseAssetUri(
                asset.DownloadUrl,
                out var uri))
        {
            return false;
        }

        string urlFileName;

        try
        {
            urlFileName =
                Uri.UnescapeDataString(
                    Path.GetFileName(
                        uri.AbsolutePath));
        }
        catch
        {
            return false;
        }

        return string.Equals(
            urlFileName,
            asset.Name,
            StringComparison.Ordinal);
    }

    private static bool TryCreateOfficialReleaseAssetUri(
        string? value,
        out Uri uri)
    {
        uri =
            null!;

        if (
            string.IsNullOrWhiteSpace(
                value) ||
            !Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var parsed) ||
            !IsStandardHttpsUri(
                parsed) ||
            !string.Equals(
                parsed.Host,
                "github.com",
                StringComparison.OrdinalIgnoreCase) ||
            !parsed.AbsolutePath.StartsWith(
                $"/{RepositoryOwner}/{RepositoryName}/releases/download/",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri =
            parsed;

        return true;
    }

    private static void ValidateApiResponseOrigin(
        HttpResponseMessage response)
    {
        var uri =
            response.RequestMessage?
                .RequestUri;

        if (
            uri is null ||
            !IsStandardHttpsUri(
                uri) ||
            !string.Equals(
                uri.Host,
                "api.github.com",
                StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(
                $"/repos/{RepositoryOwner}/{RepositoryName}/releases",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "ADT refused update metadata that did not come directly from the expected GitHub API endpoint.");
        }
    }

    private static void ValidateFinalDownloadOrigin(
        HttpResponseMessage response)
    {
        var uri =
            response.RequestMessage?
                .RequestUri;

        if (
            uri is null ||
            !IsStandardHttpsUri(
                uri) ||
            !IsApprovedGitHubAssetHost(
                uri.Host))
        {
            throw new InvalidDataException(
                "ADT refused an update download whose final HTTPS response was not served from an approved GitHub asset host.");
        }
    }

    private static bool IsApprovedGitHubAssetHost(
        string host)
    {
        return
            string.Equals(
                host,
                "github.com",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                host,
                "githubusercontent.com",
                StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(
                ".githubusercontent.com",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStandardHttpsUri(
        Uri uri)
    {
        return
            string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) &&
            uri.IsDefaultPort &&
            string.IsNullOrEmpty(
                uri.UserInfo);
    }

    private static string NormalizeOfficialReleaseHtmlUrl(
        string? value)
    {
        if (
            !Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri) ||
            !IsStandardHttpsUri(
                uri) ||
            !string.Equals(
                uri.Host,
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            return ReleasesPageUrl;
        }

        var repositoryPath =
            $"/{RepositoryOwner}/{RepositoryName}/releases";

        if (
            !(
                string.Equals(
                    uri.AbsolutePath.TrimEnd('/'),
                    repositoryPath,
                    StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith(
                    repositoryPath + "/",
                    StringComparison.OrdinalIgnoreCase)
            ))
        {
            return ReleasesPageUrl;
        }

        return uri.AbsoluteUri;
    }

    private static string NormalizeDestinationPath(
        string destinationPath,
        ReleaseAssetKind kind)
    {
        if (string.IsNullOrWhiteSpace(
                destinationPath))
        {
            throw new ArgumentException(
                "Update download destination is required.",
                nameof(destinationPath));
        }

        string destination;

        try
        {
            destination =
                Path.GetFullPath(
                    destinationPath);
        }
        catch (Exception ex)
            when (
                ex is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            throw new InvalidDataException(
                "ADT could not normalize the selected update destination.",
                ex);
        }

        var fileName =
            Path.GetFileName(
                destination);

        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            throw new InvalidOperationException(
                "Choose a valid update filename.");
        }

        var expectedExtension =
            kind ==
            ReleaseAssetKind.Installer
                ? ".exe"
                : ".zip";

        if (
            !string.Equals(
                Path.GetExtension(
                    fileName),
                expectedExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"ADT requires this update asset to be saved as a {expectedExtension} file.");
        }

        return destination;
    }

    private static void ValidateDeclaredSizes(
        AtomicReleaseAsset asset,
        long? contentLength)
    {
        if (
            contentLength is >
            MaximumDownloadBytes)
        {
            throw new InvalidDataException(
                $"ADT refused an update response larger than the {FormatByteLimit(MaximumDownloadBytes)} safety limit.");
        }

        if (
            contentLength is >
                0 &&
            asset.SizeBytes >
                0 &&
            contentLength.Value !=
                asset.SizeBytes)
        {
            throw new InvalidDataException(
                $"The GitHub download response size ({contentLength.Value} bytes) did not match the release asset metadata ({asset.SizeBytes} bytes).");
        }
    }

    private static async Task<int> ReadWithIdleTimeoutAsync(
        Stream input,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        using var idleCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        idleCancellation.CancelAfter(
            DownloadIdleTimeout);

        try
        {
            return await input.ReadAsync(
                buffer,
                idleCancellation.Token);
        }
        catch (OperationCanceledException)
            when (
                !cancellationToken.IsCancellationRequested &&
                idleCancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The ADT update download received no data for {DownloadIdleTimeout.TotalSeconds:0} seconds.");
        }
    }

    private static async Task ValidateDownloadedFileFormatAsync(
        string path,
        ReleaseAssetKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (
            kind ==
            ReleaseAssetKind.Installer)
        {
            var header =
                new byte[2];

            await using var stream =
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    useAsync: true);

            var read =
                await stream.ReadAsync(
                    header.AsMemory(
                        0,
                        header.Length),
                    cancellationToken);

            if (
                read !=
                    2 ||
                header[0] !=
                    (byte)'M' ||
                header[1] !=
                    (byte)'Z')
            {
                throw new InvalidDataException(
                    "ADT discarded the installer because the downloaded file did not have a Windows executable header.");
            }

            return;
        }

        try
        {
            using var archive =
                ZipFile.OpenRead(
                    path);

            if (
                archive.Entries.Count ==
                0)
            {
                throw new InvalidDataException(
                    "ADT discarded the portable package because the ZIP archive was empty.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
            when (
                ex is IOException or
                UnauthorizedAccessException or
                NotSupportedException)
        {
            throw new InvalidDataException(
                "ADT discarded the portable package because it could not be validated as a readable ZIP archive.",
                ex);
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 128,
                useAsync: true);

        using var sha =
            SHA256.Create();

        var hash =
            await sha.ComputeHashAsync(
                stream,
                cancellationToken);

        return Convert
            .ToHexString(
                hash)
            .ToLowerInvariant();
    }

    private static bool TryNormalizeSha256Digest(
        string? digest,
        out string sha256)
    {
        sha256 =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                digest))
        {
            return false;
        }

        const string prefix =
            "sha256:";

        if (
            !digest.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value =
            digest[prefix.Length..]
                .Trim();

        if (
            value.Length !=
                64 ||
            !value.All(
                character =>
                    Uri.IsHexDigit(
                        character)))
        {
            return false;
        }

        sha256 =
            value.ToLowerInvariant();

        return true;
    }

    private static bool TryGetAssetKind(
        string? name,
        out ReleaseAssetKind kind)
    {
        kind =
            default;

        if (
            string.IsNullOrWhiteSpace(
                name) ||
            !name.StartsWith(
                "AtomicDriftTuner-",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (
            name.EndsWith(
                "-setup.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            kind =
                ReleaseAssetKind.Installer;

            return true;
        }

        if (
            name.EndsWith(
                "-portable.zip",
                StringComparison.OrdinalIgnoreCase))
        {
            kind =
                ReleaseAssetKind.PortableZip;

            return true;
        }

        return false;
    }

    private static bool IsSafeAssetFileName(
        string name)
    {
        if (
            !string.Equals(
                Path.GetFileName(
                    name),
                name,
                StringComparison.Ordinal) ||
            name.IndexOfAny(
                Path.GetInvalidFileNameChars()) >=
            0)
        {
            return false;
        }

        return
            !name.Contains('/') &&
            !name.Contains('\\');
    }

    private static void SafeReportProgress(
        IProgress<double>? progress,
        double value)
    {
        if (progress is null)
        {
            return;
        }

        try
        {
            progress.Report(
                Math.Clamp(
                    value,
                    0,
                    100));
        }
        catch
        {
            // A UI/progress observer must never invalidate a completed download.
        }
    }

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
            // Cleanup failure must not hide the original update error.
        }
    }

    private static string FormatByteLimit(
        long bytes)
    {
        return
            $"{bytes / (1024d * 1024d):0} MB";
    }

    internal static int CompareVersionStrings(
        string left,
        string right)
    {
        var leftOk =
            AtomicVersion.TryParse(
                left,
                out var leftVersion);

        var rightOk =
            AtomicVersion.TryParse(
                right,
                out var rightVersion);

        if (
            leftOk &&
            rightOk)
        {
            return leftVersion.CompareTo(
                rightVersion);
        }

        if (
            leftOk !=
            rightOk)
        {
            return
                leftOk
                    ? 1
                    : -1;
        }

        return string.Compare(
            NormalizeVersionForFallback(
                left),
            NormalizeVersionForFallback(
                right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersionForFallback(
        string? value)
    {
        var clean =
            (value ??
             string.Empty)
            .Trim();

        if (
            clean.Length >
                0 &&
            (
                clean[0] ==
                    'v' ||
                clean[0] ==
                    'V'
            ))
        {
            clean =
                clean[1..];
        }

        return clean;
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
        public List<GitHubAssetDto> Assets { get; set; } =
            [];
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

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }

    private readonly record struct DiscoveredAssetMetadata(
        string Name,
        long SizeBytes,
        string ReleaseTag,
        AtomicVersion Version,
        string? ExpectedSha256);

    private enum ReleaseAssetKind
    {
        Installer,
        PortableZip
    }

    private readonly record struct ReleaseCandidate(
        AtomicReleaseInfo Release,
        AtomicVersion Version);

    private readonly record struct AtomicVersion(
        int Major,
        int Minor,
        int Patch,
        string[] Prerelease)
        : IComparable<AtomicVersion>
    {
        public bool IsPrerelease =>
            Prerelease.Length >
            0;

        public static bool TryParse(
            string value,
            out AtomicVersion version)
        {
            version =
                default;

            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return false;
            }

            var clean =
                value.Trim();

            if (
                clean.Length >
                    0 &&
                (
                    clean[0] ==
                        'v' ||
                    clean[0] ==
                        'V'
                ))
            {
                clean =
                    clean[1..];
            }

            if (string.IsNullOrWhiteSpace(
                    clean))
            {
                return false;
            }

            var plus =
                clean.IndexOf(
                    '+');

            if (plus >= 0)
            {
                // Build metadata does not participate in precedence, but an
                // empty metadata section is not accepted as a clean ADT tag.
                if (
                    plus ==
                    clean.Length -
                    1)
                {
                    return false;
                }

                clean =
                    clean[..plus];
            }

            var numeric =
                clean;

            var prerelease =
                string.Empty;

            var dash =
                clean.IndexOf(
                    '-');

            if (dash >= 0)
            {
                if (
                    dash ==
                    clean.Length -
                    1)
                {
                    return false;
                }

                numeric =
                    clean[..dash];

                prerelease =
                    clean[(dash + 1)..];
            }

            var parts =
                numeric.Split(
                    '.');

            if (
                parts.Length !=
                    3 ||
                !TryParseNumericIdentifier(
                    parts[0],
                    out var major) ||
                !TryParseNumericIdentifier(
                    parts[1],
                    out var minor) ||
                !TryParseNumericIdentifier(
                    parts[2],
                    out var patch))
            {
                return false;
            }

            string[] prereleaseParts;

            if (string.IsNullOrEmpty(
                    prerelease))
            {
                prereleaseParts =
                    [];
            }
            else
            {
                prereleaseParts =
                    prerelease.Split(
                        '.',
                        StringSplitOptions.None);

                if (
                    prereleaseParts.Any(
                        identifier =>
                            !IsValidPrereleaseIdentifier(
                                identifier)))
                {
                    return false;
                }
            }

            version =
                new AtomicVersion(
                    major,
                    minor,
                    patch,
                    prereleaseParts);

            return true;
        }

        private static bool TryParseNumericIdentifier(
            string value,
            out int number)
        {
            number =
                0;

            if (
                string.IsNullOrEmpty(
                    value) ||
                !value.All(
                    character =>
                        character is >=
                            '0' and <=
                            '9') ||
                (
                    value.Length >
                        1 &&
                    value[0] ==
                        '0'
                ))
            {
                return false;
            }

            return
                int.TryParse(
                    value,
                    out number) &&
                number >=
                    0;
        }

        private static bool IsValidPrereleaseIdentifier(
            string identifier)
        {
            if (string.IsNullOrEmpty(
                    identifier))
            {
                return false;
            }

            foreach (
                var character in
                identifier)
            {
                if (
                    !(
                        character is >=
                            '0' and <=
                            '9' ||
                        character is >=
                            'A' and <=
                            'Z' ||
                        character is >=
                            'a' and <=
                            'z' ||
                        character ==
                            '-'
                    ))
                {
                    return false;
                }
            }

            if (
                identifier.All(
                    char.IsDigit) &&
                identifier.Length >
                    1 &&
                identifier[0] ==
                    '0')
            {
                return false;
            }

            return true;
        }

        public int CompareTo(
            AtomicVersion other)
        {
            var compare =
                Major.CompareTo(
                    other.Major);

            if (compare != 0)
            {
                return compare;
            }

            compare =
                Minor.CompareTo(
                    other.Minor);

            if (compare != 0)
            {
                return compare;
            }

            compare =
                Patch.CompareTo(
                    other.Patch);

            if (compare != 0)
            {
                return compare;
            }

            if (
                Prerelease.Length ==
                    0 &&
                other.Prerelease.Length ==
                    0)
            {
                return 0;
            }

            if (
                Prerelease.Length ==
                0)
            {
                return 1;
            }

            if (
                other.Prerelease.Length ==
                0)
            {
                return -1;
            }

            var count =
                Math.Max(
                    Prerelease.Length,
                    other.Prerelease.Length);

            for (
                var i = 0;
                i < count;
                i++)
            {
                if (
                    i >=
                    Prerelease.Length)
                {
                    return -1;
                }

                if (
                    i >=
                    other.Prerelease.Length)
                {
                    return 1;
                }

                var a =
                    Prerelease[i];

                var b =
                    other.Prerelease[i];

                var aNumber =
                    int.TryParse(
                        a,
                        out var aInt);

                var bNumber =
                    int.TryParse(
                        b,
                        out var bInt);

                if (
                    aNumber &&
                    bNumber)
                {
                    compare =
                        aInt.CompareTo(
                            bInt);
                }
                else if (
                    aNumber !=
                    bNumber)
                {
                    compare =
                        aNumber
                            ? -1
                            : 1;
                }
                else
                {
                    compare =
                        string.Compare(
                            a,
                            b,
                            StringComparison.Ordinal);
                }

                if (compare != 0)
                {
                    return compare;
                }
            }

            return 0;
        }
    }
}
