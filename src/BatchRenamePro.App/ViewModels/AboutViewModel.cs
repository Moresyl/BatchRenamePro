using System.Reflection;
using BatchRenamePro.App.Localization;
using BatchRenamePro.App.Services;
using CommunityToolkit.Mvvm.Input;

namespace BatchRenamePro.App.ViewModels;

/// <summary>The about page: version, licence, and the links an open-source project needs.</summary>
public sealed partial class AboutViewModel
{
    private readonly IExternalLauncher _launcher;

    /// <summary>Creates the page.</summary>
    /// <param name="tokens">The token reference shown on this page.</param>
    /// <param name="update">The release state shared with the title bar.</param>
    /// <param name="launcher">Opens public project links.</param>
    public AboutViewModel(TokenPickerViewModel tokens, UpdateViewModel update, IExternalLauncher launcher)
    {
        Tokens = tokens;
        Update = update;
        _launcher = launcher;
    }

    /// <summary>The token reference, shown here as the app's built-in documentation.</summary>
    public TokenPickerViewModel Tokens { get; }

    /// <summary>Update information and commands.</summary>
    public UpdateViewModel Update { get; }

    /// <summary>The product version, without the build metadata a commit hash adds.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The runtime the app is running on.</summary>
    public static string Runtime => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    /// <summary>The operating system the app is running on.</summary>
    public static string OperatingSystem => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    /// <summary>The project's home page.</summary>
    public static string Repository => ProductLinks.Repository;

    /// <summary>Where to report a bug.</summary>
    public static string Issues => ProductLinks.Issues;

    /// <summary>The licence text.</summary>
    public static string License => ProductLinks.License;

    /// <summary>Opens a link in the default browser.</summary>
    /// <param name="url">The address to open.</param>
    [RelayCommand]
    private void OpenLink(string? url) => _launcher.Open(url);

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
