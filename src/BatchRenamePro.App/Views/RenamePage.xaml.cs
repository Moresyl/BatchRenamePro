using System.Windows;
using System.Windows.Controls;
using BatchRenamePro.App.ViewModels;

namespace BatchRenamePro.App.Views;

/// <summary>The rename workspace: the rule pipeline on the left, sources and preview on the right.</summary>
public partial class RenamePage : UserControl
{
    /// <summary>Creates the page.</summary>
    public RenamePage() => InitializeComponent();

    private RenameViewModel? Model => DataContext as RenameViewModel;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var accepted = e.Data.GetDataPresent(DataFormats.FileDrop);

        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        // DragEnter fires again for every child the pointer crosses, so the overlay is driven from
        // DragOver — which is continuous — rather than from enter/leave pairs that arrive unbalanced.
        DropOverlay.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        e.Handled = true;
        DropOverlay.Visibility = Visibility.Collapsed;

        if (Model is not { } model) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        await model.AddPathsAsync(paths).ConfigureAwait(true);
    }

    // The picker is a menu, not a form: choosing an entry should both add the rule and close the
    // flyout. The command is bound in XAML; this only takes care of the closing.
    private void OnRulePicked(object sender, RoutedEventArgs e) => AddRuleToggle.IsChecked = false;
}
