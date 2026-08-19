using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Tests;

/// <summary>
/// A rule that renames each item according to a lookup table, so a test can set up an exact
/// arrangement of moves.
/// </summary>
/// <remarks>
/// The shipped rules are deliberately uniform — one pipeline applied to every item — which makes
/// arrangements like "a becomes b while b becomes c" awkward to express with them. Those arrangements
/// are exactly what the planner's collision logic exists for, so the tests build them directly.
/// </remarks>
internal sealed class MappingRule : RenameRuleBase
{
    private readonly IReadOnlyDictionary<string, string> _map;

    public MappingRule(IReadOnlyDictionary<string, string> map) => _map = map;

    public override string TypeKey => "test.mapping";

    public override NameParts Apply(NameParts input, RenameContext context) =>
        _map.TryGetValue(input.BaseName, out var replacement) ? input with { BaseName = replacement } : input;

    public override IReadOnlyList<RuleDiagnostic> Validate() => [];

    public override IRenameRule Clone() => CopyBaseTo(new MappingRule(_map));
}
