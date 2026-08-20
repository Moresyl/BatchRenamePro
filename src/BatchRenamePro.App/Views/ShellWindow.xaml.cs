using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using BatchRenamePro.App.Interop;
using BatchRenamePro.App.Services;
using BatchRenamePro.App.ViewModels;

namespace BatchRenamePro.App.Views;

#pragma warning disable CA1001 // A window releases what it owns in OnClosed; Window is not IDisposable.

/// <summary>The application window: title bar, navigation tabs, pages, dialogs and toasts.</summary>
public partial class ShellWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ShellViewModel _model;

    private TrayIcon? _tray;

    /// <summary>Creates the window.</summary>
    /// <param name="model">The shell view model, supplied by the container.</param>
    /// <param name="theme">Decides whether the frame is drawn light or dark.</param>
    /// <param name="settings">Supplies the requested window material.</param>
    public ShellWindow(ShellViewModel model, IThemeService theme, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(settings);

        _model = model;
        _theme = theme;
        _settings = settings;

        InitializeComponent();

        DataContext = model;

        _theme.Changed += OnThemeChanged;
        _model.Dialogs.PropertyChanged += OnDialogChanged;
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // The window cannot be resized, so this is the one chance to notice it does not fit.
        WindowFrame.FitToWorkArea(this);
        WindowFrame.ForbidResizing(this);
        ApplyFrame();
    }

    /// <inheritdoc />
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // Minimized and Normal are the only two states a fixed-size window has, so there is nothing
        // to record and no caption button to relabel — only the one setting to honour.
        if (WindowState == WindowState.Minimized && _settings.Current.MinimizeToTray) HideToTray();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _theme.Changed -= OnThemeChanged;
        _model.Dialogs.PropertyChanged -= OnDialogChanged;
        _model.Dispose();

        if (_tray is not null)
        {
            _tray.Selected -= OnTraySelected;
            _tray.ContextMenuRequested -= OnTrayContextMenu;
            _tray.Dispose();
            _tray = null;
        }

        base.OnClosed(e);
    }

    /// <summary>The notification-area icon, created the first time the window has to disappear.</summary>
    /// <remarks>
    /// Deliberately not created at startup. An icon that sits in the notification area for the whole
    /// session is a different feature from one that stands in for a hidden window, and a user who
    /// never turns the setting on should never see one.
    /// </remarks>
    private TrayIcon Tray
    {
        get
        {
            if (_tray is not null) return _tray;

            _tray = new TrayIcon(Localization.Localizer.Current["app.name"]);
            _tray.Selected += OnTraySelected;
            _tray.ContextMenuRequested += OnTrayContextMenu;

            return _tray;
        }
    }

    private void ApplyFrame() =>
        _theme.ReportBackdrop(WindowFrame.Apply(this, _theme.IsDark, _settings.Current.Backdrop));

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyFrame();

    // An input dialog that opens without the caret in its text box makes the user reach for the
    // mouse to answer a question they were already typing the answer to.
    private void OnDialogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(DialogHost.Current)) return;
        if (_model.Dialogs.Current is not { IsInput: true }) return;

        Dispatcher.BeginInvoke(() => MoveFocus(new TraversalRequest(FocusNavigationDirection.First)),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HideToTray()
    {
        var tray = Tray;
        tray.Tooltip = Localization.Localizer.Current["app.name"];
        tray.Show();

        // Hidden rather than merely minimized: a taskbar button left behind next to the tray icon
        // would give the window two places to come back from, and only one of them was asked for.
        Hide();
    }

    private void RestoreFromTray()
    {
        _tray?.Hide();

        Show();
        WindowState = WindowState.Normal;
        Activate();

        // Activate() is not always enough on its own. The foreground window belongs to whatever the
        // user was doing instead, and Windows only lets a process hand the foreground over — but the
        // click that got us here was on our own tray window, so this is a call it honours.
        _ = NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void OnTraySelected(object? sender, EventArgs e) => RestoreFromTray();

    private void OnTrayContextMenu(object? sender, Point position)
    {
        if (TryFindResource("Tray.Menu") is not ContextMenu menu) return;

        // A menu whose owner is in the background never learns that the user clicked somewhere else,
        // and stays on screen until something finally takes the focus away from it.
        _ = NativeMethods.SetForegroundWindow(Tray.Handle);

        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = position.X;
        menu.VerticalOffset = position.Y;
        menu.IsOpen = true;
    }

    private void OnTrayShow(object sender, RoutedEventArgs e) => RestoreFromTray();

    private void OnTrayExit(object sender, RoutedEventArgs e) => Close();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
