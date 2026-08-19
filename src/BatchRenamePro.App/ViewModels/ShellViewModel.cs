using System.Collections.ObjectModel;
using BatchRenamePro.App.Localization;
using BatchRenamePro.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BatchRenamePro.App.ViewModels;

/// <summary>The pages the navigation rail can reach.</summary>
public enum ShellPage
{
    /// <summary>The rename workspace.</summary>
    Rename,

    /// <summary>The preset gallery.</summary>
    Presets,

    /// <summary>Completed batches, still undoable.</summary>
    History,

    /// <summary>Application settings.</summary>
    Settings,

    /// <summary>Version, licence and the token reference.</summary>
    About
}

/// <summary>One entry in the navigation rail.</summary>
/// <param name="Page">Which page it selects.</param>
/// <param name="TitleCode">Localization key for the label.</param>
/// <param name="IconKey">Resource key of the geometry drawn beside it.</param>
/// <param name="AtBottom">Whether the entry is pinned to the bottom of the rail.</param>
public sealed record NavigationItem(ShellPage Page, string TitleCode, string IconKey, bool AtBottom = false);

/// <summary>
/// The window itself: which page is showing, what the title bar says, and the two hosts that every
/// page shares — dialogs and toasts.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ILocalizer _localizer;
    private bool _disposed;

    [ObservableProperty]
    private ShellPage _page = ShellPage.Rename;

    [ObservableProperty]
    private bool _isNavigationExpanded = true;

    /// <summary>Creates the shell.</summary>
    /// <param name="rename">The rename page.</param>
    /// <param name="history">The history page.</param>
    /// <param name="settingsPage">The settings page.</param>
    /// <param name="about">The about page.</param>
    /// <param name="dialogs">The dialog host every page shares.</param>
    /// <param name="notifications">The toast host every page shares.</param>
    /// <param name="settings">Remembered window state.</param>
    /// <param name="theme">Applies the palette.</param>
    /// <param name="localizer">Translates the title and the rail.</param>
    public ShellViewModel(
        RenameViewModel rename,
        HistoryViewModel history,
        SettingsViewModel settingsPage,
        AboutViewModel about,
        DialogHost dialogs,
        INotificationService notifications,
        ISettingsService settings,
        IThemeService theme,
        ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(rename);
        ArgumentNullException.ThrowIfNull(history);

        Rename = rename;
        History = history;
        Settings = settingsPage;
        About = about;
        Dialogs = dialogs;
        Notifications = notifications;

        _settings = settings;
        _theme = theme;
        _localizer = localizer;

        IsNavigationExpanded = settings.Current.NavigationExpanded;

        // A batch run from either page invalidates the other's view of the world.
        Rename.RunCompleted += OnHistoryChanged;
        History.Reverted += OnRenameChanged;
    }

    /// <summary>The rename workspace.</summary>
    public RenameViewModel Rename { get; }

    /// <summary>The history page.</summary>
    public HistoryViewModel History { get; }

    /// <summary>The settings page.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>The about page.</summary>
    public AboutViewModel About { get; }

    /// <summary>The in-window dialog host.</summary>
    public DialogHost Dialogs { get; }

    /// <summary>The toast host.</summary>
    public INotificationService Notifications { get; }

    /// <summary>The navigation rail.</summary>
    public static IReadOnlyList<NavigationItem> Navigation { get; } =
    [
        new(ShellPage.Rename, "nav.rename", "Icon.Rename"),
        new(ShellPage.Presets, "nav.presets", "Icon.Preset"),
        new(ShellPage.History, "nav.history", "Icon.History"),
        new(ShellPage.Settings, "nav.settings", "Icon.Settings", AtBottom: true),
        new(ShellPage.About, "nav.about", "Icon.About", AtBottom: true)
    ];

    /// <summary>The entries drawn at the top of the rail.</summary>
    public static IReadOnlyList<NavigationItem> PrimaryNavigation { get; } =
        [.. Navigation.Where(item => !item.AtBottom)];

    /// <summary>The entries pinned to the bottom of the rail.</summary>
    public static IReadOnlyList<NavigationItem> SecondaryNavigation { get; } =
        [.. Navigation.Where(item => item.AtBottom)];

    /// <summary>Whether the dark palette is showing, for the title-bar toggle's icon.</summary>
    public bool IsDark => _theme.IsDark;

    /// <summary>Loads what the shell needs before the window is shown.</summary>
    public async Task InitializeAsync()
    {
        await Rename.LoadPresetsAsync().ConfigureAwait(true);
        await History.LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Switches page.</summary>
    /// <param name="page">The page to show.</param>
    [RelayCommand]
    private async Task NavigateAsync(ShellPage page)
    {
        Page = page;

        // The history is written by this process and by any other copy of it that is running, so it
        // is re-read on arrival rather than cached from startup.
        if (page is ShellPage.History) await History.LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Collapses or expands the navigation rail.</summary>
    [RelayCommand]
    private void ToggleNavigation()
    {
        IsNavigationExpanded = !IsNavigationExpanded;
        _settings.Current.NavigationExpanded = IsNavigationExpanded;
        _settings.Save();
    }

    /// <summary>Flips between the light and dark palettes from the title bar.</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        // Deliberately resolves to an explicit Light or Dark rather than cycling back through
        // System: someone reaching for this button wants the other one now, not a third state.
        var next = _theme.IsDark ? AppTheme.Light : AppTheme.Dark;

        _settings.Current.Theme = next;
        _settings.Save();
        _theme.Apply(next);

        OnPropertyChanged(nameof(IsDark));
        Settings.Refresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Rename.RunCompleted -= OnHistoryChanged;
        History.Reverted -= OnRenameChanged;

        Rename.Dispose();
        History.Dispose();
        About.Tokens.Dispose();
    }

    private async void OnHistoryChanged(object? sender, EventArgs e) =>
        await History.LoadAsync().ConfigureAwait(true);

    private void OnRenameChanged(object? sender, EventArgs e) => Rename.SchedulePreview();
}
