using System.Windows;

namespace BatchRenamePro.App.Controls;

/// <summary>
/// Placeholder text for an empty input: <c>controls:Hint.Text="{loc:T source.filter.hint}"</c>.
/// </summary>
/// <remarks>
/// An attached property rather than a subclass of <see cref="System.Windows.Controls.TextBox"/>,
/// so the hint can be added to any templated input — including the ones inside a rule editor — with
/// one attribute and without a second control type to style.
/// </remarks>
public static class Hint
{
    /// <summary>The greyed-out text shown while the input is empty.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Hint), new PropertyMetadata(string.Empty));

    /// <summary>Gets the placeholder.</summary>
    /// <param name="element">The input.</param>
    public static string GetText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)element.GetValue(TextProperty);
    }

    /// <summary>Sets the placeholder.</summary>
    /// <param name="element">The input.</param>
    /// <param name="value">The text to show while empty.</param>
    public static void SetText(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextProperty, value);
    }
}
