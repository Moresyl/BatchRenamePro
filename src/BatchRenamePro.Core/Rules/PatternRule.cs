using System.Text.Json.Serialization;
using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Rules;

/// <summary>
/// Rebuilds a name from a pattern such as <c>Holiday-{index:000}-{name}</c>.
/// </summary>
/// <remarks>
/// This is the rule that replaces the classic "overall rename" mode, and it still understands the
/// legacy <c>*</c> and <c>#</c> shorthand so existing muscle memory keeps working.
/// </remarks>
public sealed class PatternRule : RenameRuleBase
{
    /// <summary>Preset discriminator. Part of the on-disk format.</summary>
    public const string Key = "pattern";

    private string _pattern = "{name}";
    private RenameScope _scope = RenameScope.BaseName;

    /// <summary>Creates a pattern rule with default numbering.</summary>
    public PatternRule() => Forward(Sequence, nameof(Sequence));

    /// <inheritdoc />
    [JsonIgnore]
    public override string TypeKey => Key;

    /// <summary>The pattern text.</summary>
    public string Pattern
    {
        get => _pattern;
        set => Set(ref _pattern, value ?? string.Empty);
    }

    /// <summary>Which part of the name the pattern produces.</summary>
    public RenameScope Scope
    {
        get => _scope;
        set => Set(ref _scope, value);
    }

    /// <summary>Numbering used by <c>{index}</c> and <c>#</c>.</summary>
    public SequenceSettings Sequence { get; init; } = new();

    /// <inheritdoc />
    public override NameParts Apply(NameParts input, RenameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(_pattern)) return input;

        var expanded = TokenEngine.Expand(_pattern, input, context, Sequence.ToOptions());

        return _scope switch
        {
            RenameScope.FullName => NameParts.Split(expanded, context.IsDirectory),
            RenameScope.Extension => input with { Extension = expanded.Length == 0 ? string.Empty : "." + expanded.TrimStart('.') },
            _ => input with { BaseName = expanded }
        };
    }

    /// <inheritdoc />
    public override IReadOnlyList<RuleDiagnostic> Validate()
    {
        var diagnostics = new List<RuleDiagnostic>();

        if (string.IsNullOrWhiteSpace(_pattern))
            diagnostics.Add(RuleDiagnostic.Error("rule.pattern.empty", "The naming pattern cannot be empty."));

        var unknown = TokenEngine.FindUnknownTokens(_pattern);
        if (unknown.Count > 0)
        {
            diagnostics.Add(RuleDiagnostic.Warning(
                "rule.pattern.unknownToken",
                $"Unrecognized token(s) will be kept as literal text: {string.Join(", ", unknown)}."));
        }

        diagnostics.AddRange(SequenceValidation.Validate(Sequence));
        return diagnostics;
    }

    /// <inheritdoc />
    public override IRenameRule Clone() => CopyBaseTo(new PatternRule
    {
        _pattern = _pattern,
        _scope = _scope,
        Sequence = Sequence.Clone()
    });
}
