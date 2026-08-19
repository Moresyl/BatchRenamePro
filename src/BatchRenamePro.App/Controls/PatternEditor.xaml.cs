using System.Windows;
using System.Windows.Controls;
using BatchRenamePro.App.Localization;
using BatchRenamePro.App.ViewModels;

namespace BatchRenamePro.App.Controls;

/// <summary>
/// A monospaced text box for a rename pattern, with a drop-down that inserts a token at the caret.
/// </summary>
/// <remarks>
/// Inserting at the caret rather than appending is the whole point of the control: patterns are
/// built by typing around tokens — <c>{parent}_{index}</c> — and a picker that could only append
/// would have the user retyping the tail every time.
/// </remarks>
public partial class PatternEditor : UserControl
{
    /// <summary>The pattern being edited.</summary>
    public static readonly DependencyProperty PatternProperty = DependencyProperty.Register(
        nameof(Pattern),
        typeof(string),
        typeof(PatternEditor),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>The placeholder shown while the pattern is empty.</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(PatternEditor), new PropertyMetadata(string.Empty));

    // One catalog for the whole application. It is immutable apart from its translated labels, which
    // it refreshes itself, so every editor can share the same instance for the life of the process.
    private static readonly Lazy<TokenPickerViewModel> Catalog =
        new(() => new TokenPickerViewModel(Localizer.Current));

    /// <summary>Creates the editor.</summary>
    public PatternEditor() => InitializeComponent();

    /// <summary>The pattern being edited.</summary>
    public string Pattern
    {
        get => (string)GetValue(PatternProperty);
        set => SetValue(PatternProperty, value);
    }

    /// <summary>The placeholder shown while the pattern is empty.</summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>Every token the engine understands, grouped by category.</summary>
    public static IReadOnlyList<TokenGroup> TokenGroups => Catalog.Value.Groups;

    private void OnTokenClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string token }) return;

        var start = Input.SelectionStart;
        var text = Input.Text;

        Input.Text = string.Concat(text.AsSpan(0, start), token, text.AsSpan(start + Input.SelectionLength));
        Input.CaretIndex = start + token.Length;

        PickerToggle.IsChecked = false;
        Input.Focus();
    }
}
