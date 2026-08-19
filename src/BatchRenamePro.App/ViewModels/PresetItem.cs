using BatchRenamePro.App.Localization;
using BatchRenamePro.Core.Presets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BatchRenamePro.App.ViewModels;

/// <summary>A preset as the gallery shows it.</summary>
/// <remarks>
/// Built-in presets carry localization keys in <see cref="RulePreset.Name"/> and
/// <see cref="RulePreset.DescriptionCode"/>, because the core has no opinion about language; user
/// presets carry the text the user typed. Resolving that difference here keeps it out of the XAML
/// and out of the core.
/// </remarks>
public sealed class PresetItem : ObservableObject
{
    private readonly ILocalizer _localizer;

    /// <summary>Wraps a preset.</summary>
    /// <param name="preset">The preset being shown.</param>
    /// <param name="localizer">Resolves the built-in names.</param>
    public PresetItem(RulePreset preset, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(preset);

        Preset = preset;
        _localizer = localizer;
    }

    /// <summary>The preset itself.</summary>
    public RulePreset Preset { get; }

    /// <summary>Whether the preset ships with the app.</summary>
    public bool IsBuiltIn => Preset.IsBuiltIn;

    /// <summary>The name to display.</summary>
    public string Name => Preset.IsBuiltIn ? _localizer[Preset.Name] : Preset.Name;

    /// <summary>The description to display.</summary>
    public string Description => Preset.DescriptionCode.Length > 0
        ? _localizer[Preset.DescriptionCode]
        : Preset.Description;

    /// <summary>How many rules the preset contains.</summary>
    public int RuleCount => Preset.Rules.Count;

    /// <summary>Re-reads the translated text after a language change.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
