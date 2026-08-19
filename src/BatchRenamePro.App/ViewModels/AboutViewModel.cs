using System.Diagnostics;
using System.IO;
using System.Reflection;
using BatchRenamePro.App.Localization;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BatchRenamePro.App.ViewModels;

/// <summary>The about page: version, licence, and the links an open-source project needs.</summary>
public sealed partial class AboutViewModel
{
    /// <summary>Where the project lives.</summary>
    public const string RepositoryUrl = "https://github.com/batchrenamepro/batchrenamepro";

    private readonly ILogger<AboutViewModel> _logger;

    /// <summary>Creates the page.</summary>
    /// <param name="tokens">The token reference shown on this page.</param>
    /// <param name="logger">Diagnostics.</param>
    public AboutViewModel(TokenPickerViewModel tokens, ILogger<AboutViewModel> logger)
    {
        Tokens = tokens;
        _logger = logger;
    }

    /// <summary>The token reference, shown here as the app's built-in documentation.</summary>
    public TokenPickerViewModel Tokens { get; }

    /// <summary>The product version, without the build metadata a commit hash adds.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The runtime the app is running on.</summary>
    public static string Runtime => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    /// <summary>The operating system the app is running on.</summary>
    public static string OperatingSystem => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    /// <summary>The project's home page.</summary>
    public static string Repository => RepositoryUrl;

    /// <summary>Where to report a bug.</summary>
    public static string Issues => RepositoryUrl + "/issues";

    /// <summary>The licence text.</summary>
    public static string License => RepositoryUrl + "/blob/main/LICENSE";

    /// <summary>Opens a link in the default browser.</summary>
    /// <param name="url">The address to open.</param>
    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        // Only http(s) is ever launched from here: these strings are constants today, but an about
        // page that shells out to whatever it is handed is one localisation file away from trouble.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(error, "Could not open {Url}", uri);
        }
    }

    /// <summary>Copies the version and environment, for pasting into a bug report.</summary>
    /// <param name="localizer">Supplies the app name.</param>
    public static string Diagnostics(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return $"{localizer["app.name"]} {Version}\n{Runtime}\n{OperatingSystem}";
    }

    private static string ReadVersion()
    {
        var informational = typeof(AboutViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        // The SDK appends "+<commit>" to the informational version; the page wants the number only.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
