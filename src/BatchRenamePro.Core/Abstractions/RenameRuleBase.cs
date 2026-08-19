using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using BatchRenamePro.Core.Rules;

namespace BatchRenamePro.Core.Abstractions;

/// <summary>
/// Shared plumbing for rules: change notification, the enabled flag, and the polymorphic JSON
/// contract that lets a preset round-trip a heterogeneous rule list.
/// </summary>
/// <remarks>
/// Every concrete rule must be registered with a <see cref="JsonDerivedTypeAttribute"/> below and
/// carry the matching <see cref="TypeKey"/>. The discriminators are part of the on-disk preset
/// format, so changing one breaks presets saved by earlier versions.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(PatternRule), PatternRule.Key)]
[JsonDerivedType(typeof(ReplaceRule), ReplaceRule.Key)]
[JsonDerivedType(typeof(InsertRule), InsertRule.Key)]
[JsonDerivedType(typeof(RemoveRule), RemoveRule.Key)]
[JsonDerivedType(typeof(CaseRule), CaseRule.Key)]
[JsonDerivedType(typeof(ExtensionRule), ExtensionRule.Key)]
[JsonDerivedType(typeof(NumberRule), NumberRule.Key)]
[JsonDerivedType(typeof(CleanupRule), CleanupRule.Key)]
public abstract class RenameRuleBase : IRenameRule
{
    private bool _isEnabled = true;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    [JsonIgnore]
    public abstract string TypeKey { get; }

    /// <inheritdoc />
    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }

    /// <inheritdoc />
    public abstract NameParts Apply(NameParts input, RenameContext context);

    /// <inheritdoc />
    public virtual IReadOnlyList<RuleDiagnostic> Validate() => [];

    /// <inheritdoc />
    public abstract IRenameRule Clone();

    /// <summary>Assigns a backing field and raises <see cref="PropertyChanged"/> when the value actually changes.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/> for a property the rule computes rather than stores.</summary>
    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Re-raises a nested settings object's change notifications as if they came from the rule, so a
    /// listener watching the rule sees edits to composed settings such as numbering.
    /// </summary>
    protected void Forward(INotifyPropertyChanged child, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.PropertyChanged += (_, _) => Raise(propertyName);
    }

    /// <summary>Copies <see cref="IsEnabled"/> onto a freshly constructed clone.</summary>
    protected TRule CopyBaseTo<TRule>(TRule clone)
        where TRule : RenameRuleBase
    {
        ArgumentNullException.ThrowIfNull(clone);
        clone._isEnabled = _isEnabled;
        return clone;
    }
}
