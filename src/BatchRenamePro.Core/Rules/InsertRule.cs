using System.Text.Json.Serialization;
using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Rules;

/// <summary>Where <see cref="InsertRule"/> puts its text.</summary>
public enum InsertPosition
{
    /// <summary>Before everything else.</summary>
    Prefix,

    /// <summary>After everything else.</summary>
    Suffix,

    /// <summary>At a character offset counted from the start.</summary>
    AtIndex,

    /// <summary>At a character offset counted back from the end.</summary>
    FromEnd
}

/// <summary>Adds text to a name as a prefix, a suffix or at a character offset.</summary>
/// <remarks>
/// The inserted text goes through the token engine, so a suffix of <c>_{modified:yyyy-MM}</c> is
/// just as valid as a literal one.
/// </remarks>
public sealed class InsertRule : RenameRuleBase
{
    /// <summary>Preset discriminator. Part of the on-disk format.</summary>
    public const string Key = "insert";

    private string _text = string.Empty;
    private InsertPosition _position = InsertPosition.Prefix;
    private int _index;
    private RenameScope _scope = RenameScope.BaseName;

    /// <summary>Creates an insert rule with default numbering for its tokens.</summary>
    public InsertRule() => Forward(Sequence, nameof(Sequence));

    /// <inheritdoc />
    [JsonIgnore]
    public override string TypeKey => Key;

    /// <summary>The text to insert. May contain tokens.</summary>
    public string Text
    {
        get => _text;
        set => Set(ref _text, value ?? string.Empty);
    }

    /// <summary>Where the text is inserted.</summary>
    public InsertPosition Position
    {
        get => _position;
        set => Set(ref _position, value);
    }

    /// <summary>The character offset used by <see cref="InsertPosition.AtIndex"/> and <see cref="InsertPosition.FromEnd"/>.</summary>
    public int Index
    {
        get => _index;
        set => Set(ref _index, value);
    }

    /// <summary>Which part of the name receives the text.</summary>
    public RenameScope Scope
    {
        get => _scope;
        set => Set(ref _scope, value);
    }

    /// <summary>Numbering available to tokens inside <see cref="Text"/>.</summary>
    public SequenceSettings Sequence { get; init; } = new();

    /// <inheritdoc />
    public override NameParts Apply(NameParts input, RenameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_text.Length == 0) return input;

        var text = TokenEngine.Expand(_text, input, context, Sequence.ToOptions());
        return _scope.Transform(input, value => Insert(value, text));
    }

    /// <inheritdoc />
    public override IReadOnlyList<RuleDiagnostic> Validate()
    {
        var diagnostics = new List<RuleDiagnostic>();

        if (_text.Length == 0)
            diagnostics.Add(RuleDiagnostic.Error("rule.insert.emptyText", "Enter the text to add."));

        if (_position is InsertPosition.AtIndex or InsertPosition.FromEnd && _index < 0)
            diagnostics.Add(RuleDiagnostic.Error("rule.insert.negativeIndex", "The insert position cannot be negative."));

        diagnostics.AddRange(SequenceValidation.Validate(Sequence));
        return diagnostics;
    }

    /// <inheritdoc />
    public override IRenameRule Clone() => CopyBaseTo(new InsertRule
    {
        _text = _text,
        _position = _position,
        _index = _index,
        _scope = _scope,
        Sequence = Sequence.Clone()
    });

    private string Insert(string value, string text) => _position switch
    {
        InsertPosition.Prefix => text + value,
        InsertPosition.Suffix => value + text,

        // Offsets past the end clamp instead of throwing, so typing a large number in the live
        // preview degrades to "append" rather than blanking the row.
        InsertPosition.AtIndex => value.Insert(Math.Clamp(_index, 0, value.Length), text),
        InsertPosition.FromEnd => value.Insert(Math.Clamp(value.Length - _index, 0, value.Length), text),
        _ => value + text
    };
}
