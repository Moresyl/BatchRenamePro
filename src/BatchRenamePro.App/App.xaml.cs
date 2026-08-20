using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using BatchRenamePro.App.Localization;
using BatchRenamePro.App.Services;
using BatchRenamePro.App.ViewModels;
using BatchRenamePro.App.Views;
using BatchRenamePro.Core.Execution;
using BatchRenamePro.Core.Planning;
using BatchRenamePro.Core.Presets;
using BatchRenamePro.Core.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BatchRenamePro.App;

/// <summary>The application: builds the container, settles the theme and language, opens the shell.</summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>The container, once <see cref="OnStartup"/> has built it.</summary>
    /// <remarks>Exposed for the shell and for tests; never for service location from a view model.</remarks>
    public IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("The application has not started yet.");

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        _services = Build();

        var settings = _services.GetRequiredService<ISettingsService>();
        var theme = _services.GetRequiredService<IThemeService>();

        // Language and palette are settled before the first window is created: a control that reads
        // a brush or a string during construction must not have to be told about it again later.
        Localizer.Current.Use(settings.Current.Culture);
        theme.Apply(settings.Current.Theme);

        // Windows can change its app colour while we are running; "System" has to mean it.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        var shell = _services.GetRequiredService<ShellWindow>();
        MainWindow = shell;
        shell.Show();

        // After the window is up, so that a slow disk delays nothing the user can see.
        _ = _services.GetRequiredService<ShellViewModel>().InitializeAsync();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        _services?.GetService<ISettingsService>()?.Save();
        _services?.Dispose();
        _services = null;

        base.OnExit(e);
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // ---- Logging, first, so that a failure during startup is still recorded -----------------

        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new FileLoggerProvider(LogDirectory)));

        // ---- Core: file system, planning, execution, storage ------------------------------------

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFileScanner, FileScanner>();
        services.AddSingleton<IRenamePlanner, RenamePlanner>();
        services.AddSingleton<IRenameExecutor, RenameExecutor>();

        // The two stores are told where to write rather than finding out for themselves, which is
        // what lets a test point them at a temporary folder and what keeps the Core project from
        // having an opinion about %LOCALAPPDATA%.
        services.AddSingleton<IPresetStore>(provider => new JsonPresetStore(
            Path.Combine(provider.GetRequiredService<ISettingsService>().DataDirectory, "Presets")));
        services.AddSingleton<IHistoryStore>(provider => new JsonHistoryStore(
            Path.Combine(provider.GetRequiredService<ISettingsService>().DataDirectory, "History")));

        // ---- Shell services ---------------------------------------------------------------------

        services.AddSingleton<ILocalizer>(Localizer.Current);
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IExternalLauncher, ExternalLauncher>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
        services.AddSingleton<IUpdateService>(provider => new GitHubUpdateService(
            provider.GetRequiredService<HttpClient>(),
            ProductLinks.Repository));
        services.AddSingleton<DialogHost>();
        services.AddSingleton<IDialogService, DialogService>();

        // ---- View models: one each, because the shell keeps every page alive --------------------

        services.AddSingleton<TokenPickerViewModel>();
        services.AddSingleton<RenameViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<ShellViewModel>();

        services.AddSingleton<ShellWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static string LogDirectory => Path.Combine(SettingsService.DefaultDataDirectory, "Logs");

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not UserPreferenceCategory.General) return;

        _services?.GetService<IThemeService>()?.RefreshFromSystem();
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        Report(e.Exception);

        // The rename pipeline never leaves a half-applied batch behind — every executed step is in
        // the history store — so continuing is safer than tearing the process down mid-operation.
        e.Handled = true;
    }

    private void OnDomainException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) Log(exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log(e.Exception);
        e.SetObserved();
    }

    private void Log(Exception exception) =>
        _services?.GetService<ILogger<App>>()?.LogCritical(exception, "Unhandled exception");

    private void Report(Exception exception)
    {
        var notifications = _services?.GetService<INotificationService>();

        if (notifications is not null)
        {
            notifications.Show(
                string.Format(CultureInfo.CurrentCulture, Localizer.Current["error.unexpected"], exception.Message),
                NotificationKind.Error);
            return;
        }

        // Before the container exists there is nowhere to put a toast, so this is the one place the
        // application still falls back to a message box.
        MessageBox.Show(
            Localizer.Current["error.unexpected.fallback"],
            Localizer.Current["app.name"],
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
