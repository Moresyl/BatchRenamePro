using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BatchRenamePro.App.Interop;
using BatchRenamePro.App.Localization;
using BatchRenamePro.App.Services;
using BatchRenamePro.App.ViewModels;

namespace BatchRenamePro.App.Views;

/// <summary>
/// The application's own folder picker, shown in place of the shell's when the settings ask for it.
/// </summary>
/// <remarks>
/// See <see cref="FolderPickerViewModel"/> for why this exists at all. The window
/// keeps the shell's chrome conventions — its own caption strip, the app's palette, the app's
/// controls — so that it reads as part of the application rather than as a second-rate copy of a
/// system dialog.
/// </remarks>
public partial class FolderPickerWindow : Window
{
    private readonly FolderPickerViewModel _model;
    private readonly IThemeService? _theme;

    /// <summary>Creates the picker.</summary>
    /// <param name="localizer">Supplies the labels.</param>
    /// <param name="startingFolder">Where to open; empty starts at the drive list.</param>
    /// <param name="theme">Decides whether the frame is drawn light or dark; optional.</param>
    public FolderPickerWindow(ILocalizer localizer, string startingFolder, IThemeService? theme = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        _theme = theme;
        _model = new FolderPickerViewModel(localizer, startingFolder);
        _model.Finished += OnFinished;

        InitializeComponent();

        DataContext = _model;
    }

    /// <summary>What the user chose. Empty unless the dialog returned <see langword="true"/>.</summary>
    public IReadOnlyList<string> Selection => _model.Selection;

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Fixed size like the shell window, so the same two apply: on a screen too small for it there
        // is no dragging it back, and the two buttons along the bottom are the ones that go — and a
        // size command that arrives from outside is refused rather than obeyed.
        WindowFrame.FitToWorkArea(this);
        WindowFrame.ForbidResizing(this);

        // A chromeless window still has to tell DWM which way round its title bar is drawn, or the
        // resize border keeps the light colour while everything inside it goes dark.
        if (_theme is not null) _ = WindowFrame.Apply(this, _theme.IsDark, WindowBackdrop.None);

        LocationBox.Focus();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _model.Finished -= OnFinished;
        base.OnClosed(e);
    }

    // Setting DialogResult is itself what closes a modal window, so there is nothing to call after it.
    private void OnFinished(object? sender, bool accepted) => DialogResult = accepted;

    // Enter belongs to the location box while the caret is in it: the path is the thing being
    // answered, not the dialog. Marking it handled keeps the default button out of it.
    private async void OnLocationKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter) return;

        e.Handled = true;
        await _model.GoAsync().ConfigureAwait(true);
    }

    // Hit-tested rather than read off SelectedItem, so a double-click on blank space below the last
    // row does not open whichever folder happened to be highlighted.
    private async void OnFolderActivated(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(FolderList, source) is not ListBoxItem { DataContext: FolderEntry entry }) return;

        await _model.OpenAsync(entry).ConfigureAwait(true);
    }

    private async void OnShortcutSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ShortcutList.SelectedItem is not FolderEntry entry) return;

        // Cleared first, or clicking the same shortcut twice in a row is not a selection change and
        // does nothing the second time. The re-entrant call this causes leaves on the null check.
        ShortcutList.SelectedItem = null;

        await _model.OpenAsync(entry).ConfigureAwait(true);
    }
}
