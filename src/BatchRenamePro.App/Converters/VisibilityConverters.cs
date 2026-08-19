using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BatchRenamePro.App.Converters;

/// <summary>Shows an element when a flag is set, or when it is clear if <see cref="Invert"/> is on.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>Whether the meaning of the flag is reversed.</summary>
    public bool Invert { get; set; }

    /// <summary>Whether the hidden state still takes up space.</summary>
    public bool UseHidden { get; set; }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is Visibility.Visible) != Invert;
}

/// <summary>Shows an element while a value is present, or while it is absent if <see cref="Invert"/> is on.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>Whether the meaning is reversed.</summary>
    public bool Invert { get; set; }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // An empty string is "nothing to show" as far as a label is concerned, not a value.
        var present = value is not null && value is not string { Length: 0 };
        if (Invert) present = !present;
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element while a collection has items, or while it is empty if <see cref="Invert"/> is on.</summary>
/// <remarks>
/// Bound against <c>Count</c> rather than the collection itself where possible; taking the
/// collection works too but only re-evaluates when the reference changes, not when items are added.
/// </remarks>
public sealed class CountToVisibilityConverter : IValueConverter
{
    /// <summary>Whether the meaning is reversed.</summary>
    public bool Invert { get; set; }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var any = value switch
        {
            int count => count > 0,
            ICollection collection => collection.Count > 0,
            IEnumerable sequence => sequence.GetEnumerator().MoveNext(),
            _ => false
        };

        if (Invert) any = !any;
        return any ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Reports whether a value equals the converter parameter. Drives radio-style toggle buttons.</summary>
public sealed class EqualsConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    /// <inheritdoc />
    /// <remarks>
    /// Returning <see cref="Binding.DoNothing"/> for the unchecked half matters: a group of toggles
    /// raises "false" for the old selection after "true" for the new one, and writing that back
    /// would immediately undo the choice the user just made.
    /// </remarks>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return Binding.DoNothing;
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return type.IsEnum ? Enum.Parse(type, parameter.ToString()!) : parameter;
    }
}

/// <summary>Shows an element only while a value equals the converter parameter.</summary>
/// <remarks>
/// This is what switches the shell between pages. The alternative — one style per page, each with a
/// <c>DataTrigger</c> on the current page — is five near-identical blocks of markup saying the same
/// thing, and a sixth page would need a sixth. Comparing by name keeps the parameter a plain string
/// in XAML, so a page declares its own identity right where it is placed.
/// </remarks>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    /// <summary>Whether the hidden state still takes up space.</summary>
    public bool UseHidden { get; set; }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString())
            ? Visibility.Visible
            : UseHidden ? Visibility.Hidden : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
