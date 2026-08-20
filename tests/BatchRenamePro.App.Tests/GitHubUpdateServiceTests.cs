using System.Net;
using System.Net.Http;
using System.Text;
using BatchRenamePro.App.Services;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace BatchRenamePro.App.Tests;

[TestClass]
public sealed class GitHubUpdateServiceTests
{
    [TestMethod]
    public async Task CheckAsync_UnconfiguredTemplate_DoesNotSendRequest()
    {
        var handler = new RecordingHandler(_ => throw new AssertFailedException("No HTTP request was expected."));
        using var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client, PlaceholderRepository());

        var result = await service.CheckAsync("2.0.0");

        Assert.IsFalse(service.IsConfigured);
        Assert.AreEqual(UpdateCheckStatus.NotConfigured, result.Status);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task CheckAsync_NewerStableRelease_ReturnsNotesAndTrustedReleaseUrl()
    {
        var handler = JsonHandler(
            """
            {
              "tag_name": "v2.1.0",
              "name": "Batch Rename Pro 2.1",
              "body": "- Added update notifications\n- Improved Win11 support",
              "published_at": "2026-08-20T08:30:00Z",
              "draft": false,
              "prerelease": false,
              "html_url": "https://evil.example/download.exe"
            }
            """);
        using var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client, "https://github.com/Moresyl/BatchRenamePro");

        var result = await service.CheckAsync("2.0.0+build.7");

        Assert.IsTrue(service.IsConfigured);
        Assert.AreEqual(UpdateCheckStatus.Available, result.Status);
        Assert.IsNotNull(result.Update);
        Assert.AreEqual("2.1.0", result.Update.Version);
        Assert.AreEqual("Batch Rename Pro 2.1", result.Update.Title);
        StringAssert.Contains(result.Update.Notes, "update notifications");
        Assert.AreEqual("https://github.com/Moresyl/BatchRenamePro/releases/tag/v2.1.0", result.Update.ReleaseUrl);
        Assert.AreEqual(new DateTimeOffset(2026, 8, 20, 8, 30, 0, TimeSpan.Zero), result.Update.PublishedAt);
        Assert.AreEqual("https://api.github.com/repos/Moresyl/BatchRenamePro/releases/latest", handler.LastRequestUri?.AbsoluteUri);
        CollectionAssert.Contains(handler.LastAcceptHeaders, "application/vnd.github+json");
        CollectionAssert.Contains(handler.LastUserAgentHeaders, "BatchRenamePro-UpdateCheck/1.0");
        Assert.AreEqual("2026-03-10", handler.LastApiVersion);
    }

    [TestMethod]
    [DataRow("v2.0.0")]
    [DataRow("1.9.9")]
    public async Task CheckAsync_SameOrOlderRelease_IsUpToDate(string tag)
    {
        using var client = new HttpClient(JsonHandler(ReleaseJson(tag)));
        var service = new GitHubUpdateService(client, "https://github.com/Moresyl/BatchRenamePro");

        var result = await service.CheckAsync("2.0.0");

        Assert.AreEqual(UpdateCheckStatus.UpToDate, result.Status);
        Assert.IsNull(result.Update);
    }

    [TestMethod]
    [DataRow("not-a-version", false, false)]
    [DataRow("v2.1.0", true, false)]
    [DataRow("v2.1.0-beta.1", false, true)]
    [DataRow("v2.1.0-beta.1", false, false)]
    public async Task CheckAsync_InvalidStableRelease_FailsClosed(string tag, bool draft, bool prerelease)
    {
        using var client = new HttpClient(JsonHandler(ReleaseJson(tag, draft, prerelease)));
        var service = new GitHubUpdateService(client, "https://github.com/Moresyl/BatchRenamePro");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckAsync("2.0.0"));
    }

    [TestMethod]
    [DataRow("http://github.com/owner/repo")]
    [DataRow("https://example.com/owner/repo")]
    [DataRow("https://github.com/owner")]
    [DataRow("https://github.com/owner/repo/extra")]
    public void TryGetGitHubRepository_RejectsUnsupportedAddresses(string url)
    {
        var parsed = ProductLinks.TryGetGitHubRepository(url, out var owner, out var repository);

        Assert.IsFalse(parsed);
        Assert.AreEqual(string.Empty, owner);
        Assert.AreEqual(string.Empty, repository);
    }

    [TestMethod]
    public void TryGetGitHubRepository_ParsesCanonicalUrlAndGitSuffix()
    {
        var parsed = ProductLinks.TryGetGitHubRepository(
            "https://github.com/Moresyl/BatchRenamePro.git",
            out var owner,
            out var repository);

        Assert.IsTrue(parsed);
        Assert.AreEqual("Moresyl", owner);
        Assert.AreEqual("BatchRenamePro", repository);
    }

    [TestMethod]
    public void IsPlaceholderRepository_DistinguishesTemplateFromDistribution()
    {
        Assert.IsTrue(ProductLinks.IsPlaceholderRepository(PlaceholderRepository()));
        Assert.IsFalse(ProductLinks.IsPlaceholderRepository("https://github.com/Moresyl/BatchRenamePro"));
    }

    private static string PlaceholderRepository() =>
        "https://github.com/" + "batchrenamepro" + "/" + "batchrenamepro";

    private static RecordingHandler JsonHandler(string json) => new(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    });

    private static string ReleaseJson(string tag, bool draft = false, bool prerelease = false) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "{{tag}}",
          "body": "notes",
          "published_at": "2026-08-20T08:30:00Z",
          "draft": {{draft.ToString().ToLowerInvariant()}},
          "prerelease": {{prerelease.ToString().ToLowerInvariant()}}
        }
        """;

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string[] LastAcceptHeaders { get; private set; } = [];

        public string[] LastUserAgentHeaders { get; private set; } = [];

        public string? LastApiVersion { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            LastAcceptHeaders = [.. request.Headers.Accept.Select(value => value.MediaType ?? string.Empty)];
            LastUserAgentHeaders = [.. request.Headers.UserAgent.Select(value => value.ToString())];
            LastApiVersion = request.Headers.TryGetValues("X-GitHub-Api-Version", out var values)
                ? values.Single()
                : null;

            return Task.FromResult(responseFactory(request));
        }
    }
}
