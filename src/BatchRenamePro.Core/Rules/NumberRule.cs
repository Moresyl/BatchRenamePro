using System.Text.Json.Serialization;
using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Rules;

/// <summary>Numbers the selected items, either as their whole new name or alongside the old one.</summary>
/// <remarks>
/// By default the number is the name: <c>01.jpg</c>, <c>02.jpg</c>. Clearing
/// <see cref="ReplacesName"/> keeps the existing name and puts the counter beside it, which is the
/// case a full pattern rule makes needlessly wordy. Combining several of these with different
/// <see cref="SequenceSettings.GroupSize"/> values is how multi-level numbering is built.
/// </remarks>
public sealed class NumberRule : RenameRuleBase
{
    /// <summary>Preset discriminator. Part of the on-disk format.</summary>
    public const string Key = "number";

    private bool _replacesName = true;
    private InsertPosition _position = InsertPosition.Prefix;
    private int _index;
    private string _separator = string.Empty;
    private RenameScope _scope = RenameScope.BaseName;

    /// <summary>Creates a numbering rule with default settings.</summary>
    public NumberRule() => Forward(Sequence, nameof(Sequence));

    /// <inheritdoc />
    [JsonIgnore]
    public override string TypeKey => Key;

    /// <summary>Whether the number becomes the whole name instead of being added to what is there.</summary>
    /// <remarks>
    /// Set by default. Asking a rename tool to number a folder of files nearly always means
    /// <c>01.jpg, 02.jpg</c> rather than <c>01_DSC_4417.jpg</c> — the old name is the thing being
    /// got rid of, not something to carry along. Clear it to keep the name and put the counter
    /// beside it, which is what <see cref="Position"/> and <see cref="Index"/> are for.
    /// </remarks>
    public bool ReplacesName
    {
        get => _replacesName;
        set => Set(ref _replacesName, value);
    }

    /// <summary>Where the number goes. Ignored while <see cref="ReplacesName"/> is set.</summary>
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

    /// <summary>Text placed beside the number: in front of it when the rule replaces the name,
    /// between the number and the name when it does not.</summary>
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

        // Once the old name is gone there is nothing left for Position to arrange the number around,
        // so the separator simply leads: "img-" and a counter give img-01, img-02, img-03.
        if (_replacesName) return _scope.Transform(input, context, _ => _separator + number);

        return _scope.Transform(input, context, value => _position switch
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
        _replacesName = _replacesName,
        _position = _position,
        _index = _index,
        _separator = _separator,
        _scope = _scope,
        Sequence = Sequence.Clone()
    });
}
