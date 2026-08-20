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

/// <summary>Names items after a piece of text, or adds that text to the name they already have.</summary>
/// <remarks>
/// The text goes through the token engine, so <c>{modified:yyyy-MM}</c> is just as valid as a
/// literal, and <c>{name}</c> is how a replacing rule refers back to what it is replacing.
/// </remarks>
public sealed class InsertRule : RenameRuleBase
{
    /// <summary>Preset discriminator. Part of the on-disk format.</summary>
    public const string Key = "insert";

    private bool _replacesName = true;
    private string _text = string.Empty;
    private InsertPosition _position = InsertPosition.Prefix;
    private int _index;
    private RenameScope _scope = RenameScope.BaseName;

    /// <summary>Creates an insert rule with default numbering for its tokens.</summary>
    public InsertRule() => Forward(Sequence, nameof(Sequence));

    /// <inheritdoc />
    [JsonIgnore]
    public override string TypeKey => Key;

    /// <summary>Whether the text becomes the whole name instead of being added to what is there.</summary>
    /// <remarks>
    /// Set by default. Typing a name into a rename tool usually means "call them this", and a rule
    /// that quietly kept the old name around it would be answering a question nobody asked. Clear it
    /// to place the text around the existing name, which is what <see cref="Position"/> and
    /// <see cref="Index"/> are for; <c>{name}</c> does the same job from inside <see cref="Text"/>.
    /// </remarks>
    public bool ReplacesName
    {
        get => _replacesName;
        set => Set(ref _replacesName, value);
    }

    /// <summary>The text to use. May contain tokens.</summary>
    public string Text
    {
        get => _text;
        set => Set(ref _text, value ?? string.Empty);
    }

    /// <summary>Where the text is inserted. Ignored while <see cref="ReplacesName"/> is set.</summary>
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

        // Text that expands to nothing — a date token on an item whose date could not be read, say —
        // would leave the row with no name at all, so replacing stands down rather than emptying it.
        if (_replacesName && text.Length == 0) return input;

        return _scope.Transform(input, context, value => _replacesName ? text : Insert(value, text));
    }

    /// <inheritdoc />
    public override IReadOnlyList<RuleDiagnostic> Validate()
    {
        var diagnostics = new List<RuleDiagnostic>();

        if (_text.Length == 0)
            diagnostics.Add(RuleDiagnostic.Error("rule.insert.emptyText", "Enter the text to use."));

        if (_position is InsertPosition.AtIndex or InsertPosition.FromEnd && _index < 0)
            diagnostics.Add(RuleDiagnostic.Error("rule.insert.negativeIndex", "The insert position cannot be negative."));

        diagnostics.AddRange(SequenceValidation.Validate(Sequence));
        return diagnostics;
    }

    /// <inheritdoc />
    public override IRenameRule Clone() => CopyBaseTo(new InsertRule
    {
        _replacesName = _replacesName,
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
