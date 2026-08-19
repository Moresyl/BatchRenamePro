using System.Windows.Data;
using System.Windows.Markup;

namespace BatchRenamePro.App.Localization;

/// <summary>
/// Looks a string up in the active language: <c>Text="{loc:T rules.title}"</c>.
/// </summary>
/// <remarks>
/// This hands back a binding rather than a string, so switching language updates the window in
/// place instead of waiting for a restart.
/// </remarks>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    /// <summary>Creates the extension with the key supplied as a property.</summary>
    public TExtension()
    {
    }

    /// <summary>Creates the extension with a positional key.</summary>
    /// <param name="key">The string's identifier.</param>
    public TExtension(string key) => Key = key;

    /// <summary>The string's identifier.</summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]")
        {
            Source = Localizer.Current,
            Mode = BindingMode.OneWay
        }.ProvideValue(serviceProvider);
}
