using System.Text.Json.Serialization;
using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Rules;

/// <summary>Attaches a generated sequence number to a name without rewriting the rest of it.</summary>
/// <remarks>
/// This is the common case that a full pattern rule makes needlessly wordy: keep the existing name,
/// just put <c>01_</c> in front of it. Combining several of these with different
/// <see cref="SequenceSettings.GroupSize"/> values is how multi-level numbering is built.
/// </remarks>
public sealed class NumberRule : RenameRuleBase
{
    /// <summary>Preset discriminator. Part of the on-disk format.</summary>
    public const string Key = "number";

    private InsertPosition _position = InsertPosition.Prefix;
    private int _index;
    private string _separator = "_";
    private RenameScope _scope = RenameScope.BaseName;

    /// <summary>Creates a numbering rule with default settings.</summary>
    public NumberRule() => Forward(Sequence, nameof(Sequence));

    /// <inheritdoc />
    [JsonIgnore]
    public override string TypeKey => Key;

    /// <summary>Where the number goes.</summary>
    public InsertPosition Position
    {
        get => _position;
        set => Set(ref _position, value);
    }

    /// <summary>The character offset used by the offset-based positions.</summary>
    public int Index
    {
        get => _index;
        set => Set(ref _index, value);
    }

    /// <summary>Text placed between the number and the name.</summary>
    public string Separator
    {
        get => _separator;
        set => Set(ref _separator, value ?? string.Empty);
    }

    /// <summary>Which part of the name receives the number.</summary>
    public RenameScope Scope
    {
        get => _scope;
        set => Set(ref _scope, value);
    }

    /// <summary>How the number is generated.</summary>
    public SequenceSettings Sequence { get; init; } = new();

    /// <inheritdoc />
    public override NameParts Apply(NameParts input, RenameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var number = SequenceFormatter.Format(context.Index, Sequence.ToOptions());

        return _scope.Transform(input, value => _position switch
        {
            InsertPosition.Prefix => number + _separator + value,
            InsertPosition.Suffix => value + _separator + number,
            InsertPosition.AtIndex => value.Insert(Math.Clamp(_index, 0, value.Length), _separator + number + _separator),
            InsertPosition.FromEnd => value.Insert(Math.Clamp(value.Length - _index, 0, value.Length), _separator + number + _separator),
            _ => value + _separator + number
        });
    }

    /// <inheritdoc />
    public override IReadOnlyList<RuleDiagnostic> Validate() => [.. SequenceValidation.Validate(Sequence)];

    /// <inheritdoc />
    public override IRenameRule Clone() => CopyBaseTo(new NumberRule
    {
        _position = _position,
        _index = _index,
        _separator = _separator,
        _scope = _scope,
        Sequence = Sequence.Clone()
    });
}
