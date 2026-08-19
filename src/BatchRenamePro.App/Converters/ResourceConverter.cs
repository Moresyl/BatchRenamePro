using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BatchRenamePro.App.Converters;

/// <summary>Looks an application resource up by the key held in the bound value.</summary>
/// <remarks>
/// The rule catalog and the navigation rail both describe their icons as resource keys — a plain
/// string a view model can hold without referencing WPF. This converter is what turns that string
/// back into the geometry at the point of use.
/// </remarks>
public sealed class ResourceConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } key ? Application.Current?.TryFindResource(key) : null;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
