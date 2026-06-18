using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexDiscordPresence;

public sealed class GitHubReleaseChecker
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/k153636/codex-discord-presence/releases/latest");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public GitHubReleaseChecker(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("discord-presence-for-codex", AppVersion.Current.ToString()));
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<GitHubReleaseCheckResult> CheckLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return GitHubReleaseCheckResult.Failed($"GitHub release check failed with HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (release is null)
            {
                return GitHubReleaseCheckResult.Failed("GitHub release check returned an empty payload.");
            }

            if (!SemanticVersion.TryParse(release.TagName, out var latestVersion) || latestVersion is null)
            {
                return GitHubReleaseCheckResult.Failed($"GitHub release tag '{release.TagName ?? "<missing>"}' is not a valid SemVer tag.");
            }

            var currentVersion = AppVersion.Current;
            if (latestVersion.CompareTo(currentVersion) <= 0)
            {
                return GitHubReleaseCheckResult.UpToDate(currentVersion, latestVersion, release.HtmlUrl);
            }

            return GitHubReleaseCheckResult.CreateUpdateAvailable(currentVersion, latestVersion, release.HtmlUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return GitHubReleaseCheckResult.Failed("GitHub release check was canceled.");
        }
        catch (Exception ex)
        {
            return GitHubReleaseCheckResult.Failed($"GitHub release check failed: {ex.Message}");
        }
    }

    private sealed record GitHubReleaseDto(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}

public sealed record GitHubReleaseCheckResult(
    bool Succeeded,
    bool UpdateAvailable,
    SemanticVersion CurrentVersion,
    SemanticVersion? LatestVersion,
    string? LatestReleaseUrl,
    string? WarningMessage)
{
    public static GitHubReleaseCheckResult UpToDate(SemanticVersion currentVersion, SemanticVersion latestVersion, string? releaseUrl)
    {
        return new GitHubReleaseCheckResult(true, false, currentVersion, latestVersion, releaseUrl, null);
    }

    public static GitHubReleaseCheckResult CreateUpdateAvailable(SemanticVersion currentVersion, SemanticVersion latestVersion, string? releaseUrl)
    {
        return new GitHubReleaseCheckResult(true, true, currentVersion, latestVersion, releaseUrl, null);
    }

    public static GitHubReleaseCheckResult Failed(string warningMessage)
    {
        return new GitHubReleaseCheckResult(false, false, AppVersion.Current, null, null, warningMessage);
    }
}
