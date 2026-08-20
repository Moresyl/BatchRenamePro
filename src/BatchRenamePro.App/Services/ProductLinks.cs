namespace BatchRenamePro.App.Services;

/// <summary>Canonical public links for the product.</summary>
public static class ProductLinks
{
    /// <summary>The repository this build reads releases from.</summary>
    public const string Repository = "https://github.com/Moresyl/BatchRenamePro";

    /// <summary>Whether the repository placeholder has been replaced for a real distribution.</summary>
    public static bool IsRepositoryConfigured => !IsPlaceholderRepository(Repository);

    /// <summary>The repository's issue tracker.</summary>
    public static string Issues => Repository + "/issues";

    /// <summary>The repository's licence file.</summary>
    public static string License => Repository + "/blob/main/LICENSE";

    /// <summary>The repository's release list.</summary>
    public static string Releases => Repository + "/releases";

    /// <summary>Extracts a GitHub owner and repository name from the configured public URL.</summary>
    public static bool TryGetGitHubRepository(string repositoryUrl, out string owner, out string repository)
    {
        owner = string.Empty;
        repository = string.Empty;

        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2) return false;

        owner = segments[0];
        repository = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];

        return owner.Length > 0 && repository.Length > 0;
    }

    /// <summary>Whether a URL is the template's deliberately non-production repository.</summary>
    public static bool IsPlaceholderRepository(string repositoryUrl) =>
        TryGetGitHubRepository(repositoryUrl, out var owner, out var repository) &&
        string.Equals(owner, "batchrenamepro", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(repository, "batchrenamepro", StringComparison.OrdinalIgnoreCase);
}
