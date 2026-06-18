using System.Net;
using System.Net.Http;
using System.Text;
using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class GitHubReleaseCheckerTests
{
    [Fact]
    public async Task CheckLatestReleaseAsync_ReturnsUpdateAvailable_WhenLatestIsNewer()
    {
        using var client = CreateClient("""{ "tag_name": "v9.9.9", "html_url": "https://example.invalid/release" }""");
        var checker = new GitHubReleaseChecker(client);

        var result = await checker.CheckLatestReleaseAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("9.9.9", result.LatestVersion!.ToString());
    }

    [Fact]
    public async Task CheckLatestReleaseAsync_ReturnsUpToDate_WhenLatestMatchesCurrent()
    {
        var currentVersion = AppVersion.Current.ToString();
        using var client = CreateClient($@"{{ ""tag_name"": ""v{currentVersion}"", ""html_url"": ""https://example.invalid/release"" }}");
        var checker = new GitHubReleaseChecker(client);

        var result = await checker.CheckLatestReleaseAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task CheckLatestReleaseAsync_ReturnsFailure_ForInvalidSemVerTag()
    {
        using var client = CreateClient("""{ "tag_name": "latest", "html_url": "https://example.invalid/release" }""");
        var checker = new GitHubReleaseChecker(client);

        var result = await checker.CheckLatestReleaseAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not a valid SemVer", result.WarningMessage);
    }

    [Fact]
    public async Task CheckLatestReleaseAsync_ReturnsFailure_ForHttpError()
    {
        using var client = CreateClient(string.Empty, HttpStatusCode.InternalServerError);
        var checker = new GitHubReleaseChecker(client);

        var result = await checker.CheckLatestReleaseAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("HTTP 500", result.WarningMessage);
    }

    private static HttpClient CreateClient(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpClient(new StubHandler(json, statusCode))
        {
            BaseAddress = new Uri("https://api.github.com")
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _statusCode;

        public StubHandler(string json, HttpStatusCode statusCode)
        {
            _json = json;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
