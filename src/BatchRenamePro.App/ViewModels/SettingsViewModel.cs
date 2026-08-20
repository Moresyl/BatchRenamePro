using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using BatchRenamePro.App.Localization;
using BatchRenamePro.App.Services;
using BatchRenamePro.Core.Planning;
using BatchRenamePro.Core.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BatchRenamePro.App.ViewModels;

/// <summary>
/// The settings page. Every property writes through to <see cref="ISettingsService.Current"/> and
/// saves immediately.
/// </summary>
/// <remarks>
/// There is no OK button and no Apply button. A settings page that can be wrong until you press a
/// button is a settings page that gets abandoned half-changed, so each toggle takes effect and
/// persists the moment it moves.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ILocalizer _localizer;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly ILogger<SettingsViewModel> _logger;

    /// <summary>Creates the page.</summary>
    /// <param name="settings">The settings being edited.</param>
    /// <param name="theme">Applies a theme change straight away.</param>
    /// <param name="localizer">Applies a language change straight away.</param>
    /// <param name="dialogs">Confirms the reset.</param>
    /// <param name="notifications">Reports what happened.</param>
    /// <param name="logger">Diagnostics.</param>
    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        ILocalizer localizer,
        IDialogService dialogs,
        INotificationService notifications,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _theme = theme;
        _localizer = localizer;
        _dialogs = dialogs;
        _notifications = notifications;
        _logger = logger;

        _settings.Reloaded += (_, _) => OnPropertyChanged(string.Empty);
    }

    /// <summary>The languages the app ships with.</summary>
    public IReadOnlyList<LanguageOption> Languages => _localizer.Languages;

    // The enum-valued settings below are offered to the view by the EnumSource markup extension,
    // which builds a localized, language-change-aware option list straight from the enumeration —
    // so this view model deliberately keeps no hand-written lists of them.

    /// <summary>Light, dark, or whatever Windows is using.</summary>
    public AppTheme Theme
    {
        get => _settings.Current.Theme;
        set
        {
            if (!Write(value, Theme, theme => _settings.Current.Theme = theme)) return;
            _theme.Apply(value);
        }
    }

    /// <summary>The material behind the window.</summary>
    public WindowBackdrop Backdrop
    {
        get => _settings.Current.Backdrop;
        set
        {
            if (!Write(value, Backdrop, backdrop => _settings.Current.Backdrop = backdrop)) return;
            _theme.Apply(_settings.Current.Theme);
            OnPropertyChanged(nameof(BackdropAvailable));
        }
    }

    /// <summary>Whether this build of Windows can actually draw the chosen material.</summary>
    /// <remarks>
    /// Mica arrived in Windows 11 22H2. On anything older the setting still saves — so the window
    /// picks it up on a newer machine — but the page says plainly that nothing will change here.
    /// It is a fact about the machine, not about this view model, so it is static and the page
    /// reaches it with <c>{x:Static}</c>.
    /// </remarks>
    public static bool BackdropAvailable => Interop.WindowFrame.SupportsBackdrop;

    /// <summary>The interface language.</summary>
    public string Culture
    {
        get => _localizer.Culture;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == _localizer.Culture) return;

            _localizer.Use(value);
            _settings.Current.Culture = _localizer.Culture;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Whether running a batch asks first.</summary>
    public bool ConfirmBeforeRun
    {
        get => _settings.Current.ConfirmBeforeRun;
        set => Write(value, ConfirmBeforeRun, flag => _settings.Current.ConfirmBeforeRun = flag);
    }

    /// <summary>Whether the app quietly checks the public GitHub release channel after startup.</summary>
    public bool CheckForUpdatesOnStartup
    {
        get => _settings.Current.CheckForUpdatesOnStartup;
        set => Write(value, CheckForUpdatesOnStartup, flag => _settings.Current.CheckForUpdatesOnStartup = flag);
    }

    /// <summary>Whether minimizing puts the window in the notification area instead of the taskbar.</summary>
    public bool MinimizeToTray
    {
        get => _settings.Current.MinimizeToTray;
        set => Write(value, MinimizeToTray, flag => _settings.Current.MinimizeToTray = flag);
    }

    /// <summary>Which of the two folder choosers "add folder" opens.</summary>
    public FolderPickerStyle FolderPicker
    {
        get => _settings.Current.FolderPicker;
        set => Write(value, FolderPicker, style => _settings.Current.FolderPicker = style);
    }

    /// <summary>Whether adding a folder pulls in its subfolders.</summary>
    public bool RecursiveByDefault
    {
        get => _settings.Current.RecursiveByDefault;
        set => Write(value, RecursiveByDefault, flag => _settings.Current.RecursiveByDefault = flag);
    }

    /// <summary>What a folder scan collects by default.</summary>
    public ScanTarget ScanTarget
    {
        get => _settings.Current.ScanTarget;
        set => Write(value, ScanTarget, target => _settings.Current.ScanTarget = target);
    }

    /// <summary>What happens by default when two items want the same name.</summary>
    public ConflictPolicy ConflictPolicy
    {
        get => _settings.Current.ConflictPolicy;
        set => Write(value, ConflictPolicy, policy => _settings.Current.ConflictPolicy = policy);
    }

    /// <summary>How many completed batches stay undoable.</summary>
    public int HistoryLimit
    {
        get => _settings.Current.HistoryLimit;
        set => Write(Math.Clamp(value, 5, 500), HistoryLimit, limit => _settings.Current.HistoryLimit = limit);
    }

    /// <summary>Where settings, presets and history are kept.</summary>
    public string DataDirectory => _settings.DataDirectory;

    /// <summary>Re-reads every setting, for when something outside this page changed one.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>Opens the data folder in File Explorer.</summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            using var process = Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(error, "Could not open {Path}", DataDirectory);
        }
    }

    /// <summary>Throws every setting away and starts again from the defaults.</summary>
    [RelayCommand]
    private async Task ResetAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            _localizer["settings.reset"],
            _localizer["settings.resetConfirm"],
            "settings.reset",
            destructive: true).ConfigureAwait(true);

        if (!confirmed) return;

        _settings.Reset();
        _theme.Apply(_settings.Current.Theme);
        _localizer.Use(_settings.Current.Culture);

        OnPropertyChanged(string.Empty);
        _notifications.Show(_localizer["settings.resetDone"], NotificationKind.Success);
    }

    private bool Write<T>(T value, T current, Action<T> assign, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(value, current)) return false;

        assign(value);
        _settings.Save();
        OnPropertyChanged(property);

        return true;
    }
}
