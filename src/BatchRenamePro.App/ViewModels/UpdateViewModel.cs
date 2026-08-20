using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using BatchRenamePro.App.Localization;
using BatchRenamePro.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BatchRenamePro.App.ViewModels;

/// <summary>Owns the application-wide update state shared by the title bar and About page.</summary>
public sealed partial class UpdateViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(1);

    private readonly IUpdateService _updates;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly ILocalizer _localizer;
    private readonly IExternalLauncher _launcher;
    private readonly ILogger<UpdateViewModel> _logger;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckButtonText))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowBadge))]
    [NotifyPropertyChangedFor(nameof(LatestVersion))]
    [NotifyPropertyChangedFor(nameof(ReleaseTitle))]
    [NotifyPropertyChangedFor(nameof(ReleaseNotes))]
    [NotifyPropertyChangedFor(nameof(PublishedAt))]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    private AppUpdateInfo? _availableUpdate;

    /// <summary>Creates the update state.</summary>
    public UpdateViewModel(
        IUpdateService updates,
        ISettingsService settings,
        INotificationService notifications,
        ILocalizer localizer,
        IExternalLauncher launcher,
        ILogger<UpdateViewModel> logger)
    {
        _updates = updates;
        _settings = settings;
        _notifications = notifications;
        _localizer = localizer;
        _launcher = launcher;
        _logger = logger;

        _localizer.PropertyChanged += OnLanguageChanged;
        _settings.Reloaded += OnSettingsReloaded;
    }

    /// <summary>The installed product version.</summary>
    public static string CurrentVersion => AboutViewModel.Version;

    /// <summary>Whether a newer stable release is known.</summary>
    public bool HasUpdate => AvailableUpdate is not null;

    /// <summary>Whether the compact title-bar affordance should be visible.</summary>
    public bool ShowBadge => HasUpdate && !IsDismissed;

    /// <summary>The newest known version, or an em dash before a successful check.</summary>
    public string LatestVersion => AvailableUpdate?.Version ?? "—";

    /// <summary>The human-friendly release title.</summary>
    public string ReleaseTitle => AvailableUpdate?.Title ?? string.Empty;

    /// <summary>The release notes shown in the application.</summary>
    public string ReleaseNotes => string.IsNullOrWhiteSpace(AvailableUpdate?.Notes)
        ? _localizer["update.notes.empty"]
        : AvailableUpdate.Notes;

    /// <summary>Localized publication date.</summary>
    public string PublishedAt => AvailableUpdate?.PublishedAt is { } date
        ? date.ToLocalTime().ToString("D", CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>Accessible title-bar description.</summary>
    public string BadgeText => _localizer.Format("update.badge", LatestVersion);

    /// <summary>Label that follows the asynchronous check state.</summary>
    public string CheckButtonText => _localizer[IsChecking ? "update.checking" : "update.check"];

    private bool IsDismissed => AvailableUpdate is { } update && string.Equals(
        _settings.Current.DismissedUpdateVersion,
        update.Version,
        StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs the quiet, delayed startup check when the user has left it enabled.</summary>
    public async Task CheckOnStartupAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Current.CheckForUpdatesOnStartup || !_updates.IsConfigured) return;

        await Task.Delay(StartupDelay, cancellationToken).ConfigureAwait(true);
        await CheckCoreAsync(manual: false, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Checks immediately and always reports the outcome.</summary>
    [RelayCommand]
    private Task CheckAsync() => CheckCoreAsync(manual: true, CancellationToken.None);

    /// <summary>Opens the exact release page on GitHub.</summary>
    [RelayCommand]
    private void OpenRelease()
    {
        if (AvailableUpdate is { } update) _launcher.Open(update.ReleaseUrl);
    }

    /// <summary>Hides the current release badge until a newer version is published.</summary>
    [RelayCommand]
    private void Dismiss()
    {
        if (AvailableUpdate is not { } update) return;

        _settings.Current.DismissedUpdateVersion = update.Version;
        _settings.Save();
        OnPropertyChanged(nameof(ShowBadge));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localizer.PropertyChanged -= OnLanguageChanged;
        _settings.Reloaded -= OnSettingsReloaded;
        _checkGate.Dispose();
    }

    private async Task CheckCoreAsync(bool manual, CancellationToken cancellationToken)
    {
        if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(true)) return;

        IsChecking = true;
        try
        {
            var result = await _updates.CheckAsync(CurrentVersion, cancellationToken).ConfigureAwait(true);
            switch (result.Status)
            {
                case UpdateCheckStatus.NotConfigured:
                    if (manual) _notifications.Show(_localizer["update.unconfigured"], NotificationKind.Warning);
                    break;

                case UpdateCheckStatus.UpToDate:
                    AvailableUpdate = null;
                    if (manual) _notifications.Show(_localizer["update.upToDate"], NotificationKind.Success);
                    break;

                case UpdateCheckStatus.Available when result.Update is not null:
                    AvailableUpdate = result.Update;
                    if (manual || !IsDismissed) ShowAvailableToast();
                    break;
            }
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(error, "Update check failed");
            if (manual && !cancellationToken.IsCancellationRequested)
                _notifications.Show(_localizer["update.failed"], NotificationKind.Error);
        }
        finally
        {
            IsChecking = false;
            _checkGate.Release();
        }
    }

    private void ShowAvailableToast()
    {
        if (AvailableUpdate is not { } update) return;

        _notifications.Show(
            _localizer.Format("update.availableToast", update.Version),
            NotificationKind.Information,
            _localizer["update.openGithub"],
            () =>
            {
                _launcher.Open(update.ReleaseUrl);
                return Task.CompletedTask;
            });
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not ("Item[]" or nameof(ILocalizer.Culture))) return;

        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(PublishedAt));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(CheckButtonText));
    }

    private void OnSettingsReloaded(object? sender, EventArgs e) => OnPropertyChanged(nameof(ShowBadge));
}
