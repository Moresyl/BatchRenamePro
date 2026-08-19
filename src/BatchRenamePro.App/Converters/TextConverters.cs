using System.Globalization;
using System.Windows.Data;
using BatchRenamePro.App.Localization;

namespace BatchRenamePro.App.Converters;

/// <summary>
/// Turns an enum value into words, by looking up <c>enum.{TypeName}.{ValueName}</c>.
/// </summary>
/// <remarks>
/// This is what lets a combo box bind straight to <c>CaseMode.Title</c> and still read
/// "Title Case" — no per-enum wrapper view models, and one place to translate them all.
/// </remarks>
public sealed class EnumTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        var type = value.GetType();
        return type.IsEnum ? Localizer.Current[$"enum.{type.Name}.{value}"] : value.ToString() ?? string.Empty;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Fills a localized template's placeholders with the bound value: <c>"{0} selected"</c>.</summary>
public sealed class FormatConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is string key ? Localizer.Current.Format(key, value) : value?.ToString() ?? string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Looks a localization key up at runtime, for keys that are only known once data arrives.</summary>
public sealed class LocalizeConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && key.Length > 0 ? Localizer.Current[key] : string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Writes a byte count the way a file manager does.</summary>
public sealed class FileSizeConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes || bytes < 0) return string.Empty;

        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        // Bytes and kilobytes never need a decimal place; megabytes and up always look wrong without one.
        return unit <= 1
            ? string.Create(culture, $"{Math.Round(size)} {Units[unit]}")
            : string.Create(culture, $"{size:0.#} {Units[unit]}");
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Writes a timestamp as "just now", "14 minutes ago" or a plain date, whichever is clearest.</summary>
public sealed class RelativeTimeConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset moment || moment == default) return string.Empty;

        var elapsed = DateTimeOffset.Now - moment;
        var localizer = Localizer.Current;

        return elapsed switch
        {
            { TotalSeconds: < 60 } => localizer["time.justNow"],
            { TotalMinutes: < 60 } => localizer.Format("time.minutesAgo", (int)elapsed.TotalMinutes),
            { TotalHours: < 24 } => localizer.Format("time.hoursAgo", (int)elapsed.TotalHours),
            { TotalDays: < 7 } => localizer.Format("time.daysAgo", (int)elapsed.TotalDays),
            _ => moment.LocalDateTime.ToString("g", culture)
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
