using System.ComponentModel;
using System.Windows.Markup;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BatchRenamePro.App.Localization;

/// <summary>One value of an enumeration, with the label a combo box should show for it.</summary>
public sealed class EnumOption : ObservableObject
{
    internal EnumOption(Enum value)
    {
        Value = value;
        Key = $"enum.{value.GetType().Name}.{value}";
    }

    /// <summary>The underlying value, bound to through <c>SelectedValuePath</c>.</summary>
    public Enum Value { get; }

    /// <summary>The localization key this option's label comes from.</summary>
    public string Key { get; }

    /// <summary>The label, in the current language.</summary>
    public string Text => Localizer.Current[Key];

    /// <summary>The label again: a combo box entry's automation peer reads its name from here.</summary>
    public override string ToString() => Text;

    internal void Refresh() => OnPropertyChanged(nameof(Text));
}

/// <summary>
/// Turns an enumeration into a bindable, localized list of options — the item source behind every
/// combo box in the application that picks an enum value.
/// </summary>
/// <remarks>
/// The lists are cached per type and handed out by reference, which is what makes a language change
/// reach combo boxes that are already on screen: the same option objects raise
/// <see cref="INotifyPropertyChanged"/> and every binding to them re-reads its text. Building a
/// fresh list per markup extension would leave already-rendered drop-downs in the old language.
/// </remarks>
public static class EnumOptions
{
    // Written and read on the UI thread only: XAML parsing and binding both run there.
    private static readonly Dictionary<Type, IReadOnlyList<EnumOption>> Cache = [];

    private static ILocalizer? _watching;

    /// <summary>Returns the options for an enumeration, in declaration order.</summary>
    /// <param name="enumType">The enumeration to list.</param>
    public static IReadOnlyList<EnumOption> For(Type enumType)
    {
        ArgumentNullException.ThrowIfNull(enumType);

        if (!enumType.IsEnum)
            throw new ArgumentException($"{enumType.Name} is not an enumeration.", nameof(enumType));

        Watch();

        if (Cache.TryGetValue(enumType, out var cached)) return cached;

        var options = Enum.GetValues(enumType).Cast<Enum>().Select(value => new EnumOption(value)).ToArray();
        Cache[enumType] = options;

        return options;
    }

    private static void Watch()
    {
        var localizer = Localizer.Current;
        if (ReferenceEquals(_watching, localizer)) return;

        _watching?.PropertyChanged -= OnLanguageChanged;
        localizer.PropertyChanged += OnLanguageChanged;
        _watching = localizer;
    }

    private static void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var options in Cache.Values)
        {
            foreach (var option in options) option.Refresh();
        }
    }
}

/// <summary>
/// Supplies a combo box with the options for an enumeration:
/// <c>ItemsSource="{loc:EnumSource {x:Type core:RenameScope}}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(IReadOnlyList<EnumOption>))]
public sealed class EnumSourceExtension : MarkupExtension
{
    /// <summary>Creates an unbound extension. The type must then be set as a property.</summary>
    public EnumSourceExtension()
    {
    }

    /// <summary>Creates the extension for one enumeration.</summary>
    /// <param name="enumType">The enumeration to list.</param>
    public EnumSourceExtension(Type enumType) => EnumType = enumType;

    /// <summary>The enumeration to list.</summary>
    public Type? EnumType { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        EnumType is null ? Array.Empty<EnumOption>() : EnumOptions.For(EnumType);
}
