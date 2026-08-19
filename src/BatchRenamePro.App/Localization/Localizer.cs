using System.ComponentModel;
using System.Globalization;

namespace BatchRenamePro.App.Localization;

/// <summary>Turns a string key into words in the language the user picked.</summary>
public interface ILocalizer : INotifyPropertyChanged
{
    /// <summary>The active language, as a BCP-47 tag.</summary>
    string Culture { get; }

    /// <summary>The languages the application ships, in display order.</summary>
    IReadOnlyList<LanguageOption> Languages { get; }

    /// <summary>Looks up a key. Bindable, so the UI re-reads it when the language changes.</summary>
    /// <param name="key">The string's identifier.</param>
    string this[string key] { get; }

    /// <summary>Looks up a key and fills in its placeholders.</summary>
    /// <param name="key">The string's identifier.</param>
    /// <param name="arguments">Values for <c>{0}</c>, <c>{1}</c> and so on.</param>
    string Format(string key, params object?[] arguments);

    /// <summary>Switches language and tells every binding to re-read.</summary>
    /// <param name="culture">A BCP-47 tag. Unknown languages fall back to English.</param>
    void Use(string culture);
}

/// <inheritdoc cref="ILocalizer" />
public sealed class Localizer : ILocalizer
{
    /// <summary>
    /// The instance XAML binds against.
    /// </summary>
    /// <remarks>
    /// A static handle is not how the rest of the application finds its services — everything else
    /// is constructor-injected — but a XAML markup extension is constructed by the parser, which
    /// has no container to ask. Composition root sets this once at startup.
    /// </remarks>
    public static ILocalizer Current { get; internal set; } = new Localizer();

    private string _culture = StringCatalog.FallbackCulture;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public string Culture => _culture;

    /// <inheritdoc />
    public IReadOnlyList<LanguageOption> Languages => StringCatalog.Languages;

    /// <inheritdoc />
    public string this[string key] => StringCatalog.Get(_culture, key);

    /// <inheritdoc />
    public string Format(string key, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var template = this[key];

        // A translator who mistypes a placeholder should not take the window down with them.
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <inheritdoc />
    public void Use(string culture)
    {
        var resolved = Resolve(culture);
        if (string.Equals(resolved, _culture, StringComparison.OrdinalIgnoreCase)) return;

        _culture = resolved;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(resolved);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));

        // "Item[]" is the name WPF listens for to invalidate every indexer binding at once, which is
        // what makes the whole window change language without being rebuilt.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>Picks the best shipped language for a request, or for the machine's own settings.</summary>
    /// <param name="culture">A BCP-47 tag, or empty to follow Windows.</param>
    public static string Resolve(string? culture)
    {
        var requested = string.IsNullOrWhiteSpace(culture)
            ? CultureInfo.CurrentUICulture.Name
            : culture;

        if (StringCatalog.IsSupported(requested)) return requested;

        // "zh-Hans-CN" and "zh-TW" should both land on the Chinese table rather than English.
        var language = requested.Split('-', StringSplitOptions.RemoveEmptyEntries) is [var first, ..] ? first : requested;
        var match = StringCatalog.Languages.FirstOrDefault(
            option => option.Culture.StartsWith(language, StringComparison.OrdinalIgnoreCase));

        return match?.Culture ?? StringCatalog.FallbackCulture;
    }
}
