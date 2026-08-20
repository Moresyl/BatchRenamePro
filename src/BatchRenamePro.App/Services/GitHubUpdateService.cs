using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatchRenamePro.App.Services;

/// <summary>The outcome of asking the release channel for a newer version.</summary>
public enum UpdateCheckStatus
{
    /// <summary>The distribution has not been connected to a real repository yet.</summary>
    NotConfigured,

    /// <summary>The installed version is current.</summary>
    UpToDate,

    /// <summary>A newer stable release exists.</summary>
    Available
}

/// <summary>One published application update.</summary>
/// <param name="Version">Semantic version without a leading <c>v</c>.</param>
/// <param name="Title">Release title.</param>
/// <param name="Notes">Release notes supplied by GitHub.</param>
/// <param name="PublishedAt">When the release was published.</param>
/// <param name="ReleaseUrl">The exact GitHub release page.</param>
/// <param name="TagName">The exact trusted release tag used to locate signed assets.</param>
public sealed record AppUpdateInfo(
    string Version,
    string Title,
    string Notes,
    DateTimeOffset? PublishedAt,
    string ReleaseUrl,
    string TagName);

/// <summary>Result returned by an update channel.</summary>
/// <param name="Status">Whether an update exists.</param>
/// <param name="Update">The release when <paramref name="Status"/> is available.</param>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, AppUpdateInfo? Update = null);

/// <summary>Checks the public release channel without downloading or executing anything.</summary>
public interface IUpdateService
{
    /// <summary>Whether this build points at a real release repository.</summary>
    bool IsConfigured { get; }

    /// <summary>Checks the latest stable release.</summary>
    Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);
}

/// <summary>Reads the latest public GitHub Release for the configured repository.</summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string GitHubApiVersion = "2026-03-10";

    private readonly HttpClient _client;
    private readonly string _repositoryUrl;
    private readonly bool _isPlaceholder;
    private readonly string _owner = string.Empty;
    private readonly string _repository = string.Empty;

    /// <summary>Creates a GitHub release reader.</summary>
    /// <param name="client">HTTP transport, replaceable in tests.</param>
    /// <param name="repositoryUrl">Public <c>https://github.com/owner/repository</c> URL.</param>
    public GitHubUpdateService(HttpClient client, string repositoryUrl)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);

        _client = client;
        _repositoryUrl = repositoryUrl.TrimEnd('/');
        _isPlaceholder = ProductLinks.IsPlaceholderRepository(_repositoryUrl);

        if (ProductLinks.TryGetGitHubRepository(_repositoryUrl, out var owner, out var repository))
        {
            _owner = owner;
            _repository = repository;
        }
    }

    /// <inheritdoc />
    public bool IsConfigured => !_isPlaceholder && _owner.Length > 0 && _repository.Length > 0;

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);
        if (!IsConfigured) return new(UpdateCheckStatus.NotConfigured);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repository)}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("BatchRenamePro-UpdateCheck/1.0");
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new HttpRequestException("The configured repository has no published release.", null, response.StatusCode);
        }

        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync(content, UpdateJsonContext.Default.GitHubRelease, cancellationToken)
            .ConfigureAwait(false);

        if (release is null || release.Draft || release.Prerelease ||
            !TryParseVersion(release.TagName, allowPrerelease: false, out var latest))
        {
            throw new InvalidOperationException("GitHub returned an invalid stable release.");
        }

        if (!TryParseVersion(currentVersion, allowPrerelease: true, out var current))
        {
            throw new InvalidOperationException("The installed application version is invalid.");
        }

        if (latest.CompareTo(current) <= 0) return new(UpdateCheckStatus.UpToDate);

        var normalizedVersion = latest.ToString(3);
        var escapedTag = string.Join('/', release.TagName.Split('/').Select(Uri.EscapeDataString));
        var releaseUrl = $"{_repositoryUrl}/releases/tag/{escapedTag}";
        var title = string.IsNullOrWhiteSpace(release.Name) ? $"v{normalizedVersion}" : release.Name.Trim();

        return new(
            UpdateCheckStatus.Available,
            new AppUpdateInfo(
                normalizedVersion,
                title,
                release.Body?.Trim() ?? string.Empty,
                release.PublishedAt,
                releaseUrl,
                release.TagName));
    }

    private static bool TryParseVersion(string? value, bool allowPrerelease, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];

        var prerelease = normalized.IndexOf('-');
        if (prerelease >= 0)
        {
            if (!allowPrerelease) return false;
            normalized = normalized[..prerelease];
        }

        var metadata = normalized.IndexOf('+');
        if (metadata >= 0) normalized = normalized[..metadata];

        var components = normalized.Split('.');
        if (components.Length is < 2 or > 3 || components.Any(component =>
                !int.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return false;
        }

        normalized = components.Length switch
        {
            2 => normalized + ".0",
            _ => normalized
        };

        return Version.TryParse(normalized, out version!);
    }
}

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease);

[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;
