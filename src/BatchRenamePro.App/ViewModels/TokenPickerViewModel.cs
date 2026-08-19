using System.Collections.ObjectModel;
using System.ComponentModel;
using BatchRenamePro.App.Localization;
using BatchRenamePro.Core.Tokens;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BatchRenamePro.App.ViewModels;

/// <summary>One token in the picker.</summary>
public sealed class TokenItem : ObservableObject
{
    private readonly ILocalizer _localizer;

    /// <summary>Wraps a token descriptor.</summary>
    /// <param name="descriptor">The token as the engine describes it.</param>
    /// <param name="localizer">Translates the description.</param>
    public TokenItem(TokenDescriptor descriptor, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        Descriptor = descriptor;
        _localizer = localizer;
    }

    /// <summary>The engine's description of the token.</summary>
    public TokenDescriptor Descriptor { get; }

    /// <summary>The literal text inserted into the pattern.</summary>
    public string Token => Descriptor.Token;

    /// <summary>What the token does, in the user's language.</summary>
    public string Description => _localizer[Descriptor.DescriptionCode];

    /// <summary>Re-reads the description after a language change.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Description));
}

/// <summary>A group of tokens under one heading.</summary>
public sealed class TokenGroup : ObservableObject
{
    private readonly ILocalizer _localizer;

    /// <summary>Creates a group.</summary>
    /// <param name="category">The category key, as used by <see cref="TokenDescriptor.Category"/>.</param>
    /// <param name="tokens">The tokens in it.</param>
    /// <param name="localizer">Translates the heading.</param>
    public TokenGroup(string category, IReadOnlyList<TokenItem> tokens, ILocalizer localizer)
    {
        Category = category;
        Tokens = tokens;
        _localizer = localizer;
    }

    /// <summary>The category key.</summary>
    public string Category { get; }

    /// <summary>The heading shown above the group.</summary>
    public string Title => _localizer[$"tokens.category.{Category}"];

    /// <summary>The tokens in this group.</summary>
    public IReadOnlyList<TokenItem> Tokens { get; }

    /// <summary>Re-reads the heading after a language change.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        foreach (var token in Tokens) token.Refresh();
    }
}

/// <summary>
/// The token reference: every placeholder a pattern can contain, grouped and explained.
/// </summary>
/// <remarks>
/// Built once from <see cref="TokenEngine.Catalog"/>, which is the same list the engine expands. A
/// token added to the engine appears here without anyone remembering to update a second list.
/// </remarks>
public sealed class TokenPickerViewModel : IDisposable
{
    private readonly ILocalizer _localizer;
    private bool _disposed;

    /// <summary>Builds the reference.</summary>
    /// <param name="localizer">Translates the headings and descriptions.</param>
    public TokenPickerViewModel(ILocalizer localizer)
    {
        _localizer = localizer;

        Groups = [.. TokenEngine.Catalog
            .GroupBy(descriptor => descriptor.Category)
            .Select(group => new TokenGroup(
                group.Key,
                [.. group.Select(descriptor => new TokenItem(descriptor, localizer))],
                localizer))];

        _localizer.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>The tokens, grouped by category in catalog order.</summary>
    public ObservableCollection<TokenGroup> Groups { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _localizer.PropertyChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var group in Groups) group.Refresh();
    }
}
