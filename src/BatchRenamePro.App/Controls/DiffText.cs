using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace BatchRenamePro.App.Controls;

/// <summary>Which side of the comparison a run of text is showing.</summary>
public enum DiffSide
{
    /// <summary>The name as it is now; the part that is going away is highlighted.</summary>
    Before,

    /// <summary>The name as it will be; the part that is new is highlighted.</summary>
    After
}

/// <summary>
/// Highlights the part of a file name that a rename actually changes.
/// </summary>
/// <remarks>
/// Attached to a plain <see cref="TextBlock"/> rather than written as a control, so the preview list
/// keeps text trimming, selection colours and virtualization for free. The comparison is a common
/// prefix and suffix rather than a real diff: renames are overwhelmingly "the middle bit changed",
/// and a character-level diff on a file name produces speckled highlighting that is harder to read
/// than no highlighting at all.
/// </remarks>
public static class DiffText
{
    /// <summary>The name before the rename.</summary>
    public static readonly DependencyProperty OriginalProperty = DependencyProperty.RegisterAttached(
        "Original", typeof(string), typeof(DiffText),
        new PropertyMetadata(string.Empty, OnChanged));

    /// <summary>The name after the rename.</summary>
    public static readonly DependencyProperty ProposedProperty = DependencyProperty.RegisterAttached(
        "Proposed", typeof(string), typeof(DiffText),
        new PropertyMetadata(string.Empty, OnChanged));

    /// <summary>Which of the two names this text block is showing.</summary>
    public static readonly DependencyProperty SideProperty = DependencyProperty.RegisterAttached(
        "Side", typeof(DiffSide), typeof(DiffText),
        new PropertyMetadata(DiffSide.After, OnChanged));

    /// <summary>The background painted behind the changed run.</summary>
    public static readonly DependencyProperty HighlightProperty = DependencyProperty.RegisterAttached(
        "Highlight", typeof(Brush), typeof(DiffText),
        new PropertyMetadata(null, OnChanged));

    /// <summary>Gets the name before the rename.</summary>
    /// <param name="element">The text block.</param>
    public static string GetOriginal(DependencyObject element) => (string)Value(element, OriginalProperty);

    /// <summary>Sets the name before the rename.</summary>
    /// <param name="element">The text block.</param>
    /// <param name="value">The name.</param>
    public static void SetOriginal(DependencyObject element, string value) => Set(element, OriginalProperty, value);

    /// <summary>Gets the name after the rename.</summary>
    /// <param name="element">The text block.</param>
    public static string GetProposed(DependencyObject element) => (string)Value(element, ProposedProperty);

    /// <summary>Sets the name after the rename.</summary>
    /// <param name="element">The text block.</param>
    /// <param name="value">The name.</param>
    public static void SetProposed(DependencyObject element, string value) => Set(element, ProposedProperty, value);

    /// <summary>Gets which name is being shown.</summary>
    /// <param name="element">The text block.</param>
    public static DiffSide GetSide(DependencyObject element) => (DiffSide)Value(element, SideProperty);

    /// <summary>Sets which name is being shown.</summary>
    /// <param name="element">The text block.</param>
    /// <param name="value">The side.</param>
    public static void SetSide(DependencyObject element, DiffSide value) => Set(element, SideProperty, value);

    /// <summary>Gets the highlight brush.</summary>
    /// <param name="element">The text block.</param>
    public static Brush? GetHighlight(DependencyObject element) => (Brush?)Value(element, HighlightProperty);

    /// <summary>Sets the highlight brush.</summary>
    /// <param name="element">The text block.</param>
    /// <param name="value">The brush.</param>
    public static void SetHighlight(DependencyObject element, Brush? value) => Set(element, HighlightProperty, value);

    private static object Value(DependencyObject element, DependencyProperty property)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(property);
    }

    private static void Set(DependencyObject element, DependencyProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(property, value);
    }

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block) return;

        var original = GetOriginal(block) ?? string.Empty;
        var proposed = GetProposed(block) ?? string.Empty;
        var side = GetSide(block);
        var text = side is DiffSide.Before ? original : proposed;

        block.Inlines.Clear();

        var highlight = GetHighlight(block);
        if (highlight is null || string.Equals(original, proposed, StringComparison.Ordinal))
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        var (start, length) = Segment(original, proposed, side);
        if (length <= 0)
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        if (start > 0) block.Inlines.Add(new Run(text[..start]));

        block.Inlines.Add(new Run(text.Substring(start, length))
        {
            Background = highlight,
            FontWeight = FontWeights.SemiBold
        });

        var end = start + length;
        if (end < text.Length) block.Inlines.Add(new Run(text[end..]));
    }

    /// <summary>Finds the run of characters that differs, as an offset into the requested side.</summary>
    /// <param name="original">The name before.</param>
    /// <param name="proposed">The name after.</param>
    /// <param name="side">Which name the offset should be relative to.</param>
    /// <remarks>
    /// Ordinal throughout: these are file names, and a culture-sensitive comparison would call
    /// "a" and "á" the same character and highlight the wrong span.
    /// </remarks>
    public static (int Start, int Length) Segment(string original, string proposed, DiffSide side)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(proposed);

        var shortest = Math.Min(original.Length, proposed.Length);

        var prefix = 0;
        while (prefix < shortest && original[prefix] == proposed[prefix]) prefix++;

        // Stop the suffix scan where the prefix ended, or the two would overlap on a name like
        // "aaa" -> "aaaa" and produce a negative length.
        var suffix = 0;
        while (suffix < shortest - prefix && original[^(suffix + 1)] == proposed[^(suffix + 1)]) suffix++;

        var text = side is DiffSide.Before ? original : proposed;
        return (prefix, text.Length - prefix - suffix);
    }
}
