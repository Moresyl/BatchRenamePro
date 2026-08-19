using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using BatchRenamePro.App.Interop;
using BatchRenamePro.App.Services;
using BatchRenamePro.App.ViewModels;

namespace BatchRenamePro.App.Views;

/// <summary>The application window: title bar, navigation rail, pages, dialogs and toasts.</summary>
public partial class ShellWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ShellViewModel _model;

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

        // Without this a chromeless window maximizes to the whole monitor and hides the taskbar.
        WindowFrame.TrackMaximizeBounds(this);
        ApplyFrame();
    }

    /// <inheritdoc />
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        var maximized = WindowState == WindowState.Maximized;

        MaximizeButton.Tag = TryFindResource(maximized ? "Icon.Window.Restore" : "Icon.Window.Maximize");
        MaximizeButton.ToolTip = Localization.Localizer.Current[maximized ? "window.restore" : "window.maximize"];
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _theme.Changed -= OnThemeChanged;
        _model.Dialogs.PropertyChanged -= OnDialogChanged;
        _model.Dispose();

        base.OnClosed(e);
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

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
