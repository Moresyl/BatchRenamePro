using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Rules;

namespace BatchRenamePro.App.ViewModels;

/// <summary>One entry in the "add rule" menu.</summary>
/// <param name="TypeKey">The rule's stable discriminator, matching <see cref="IRenameRule.TypeKey"/>.</param>
/// <param name="NameCode">Localization key for the rule's name.</param>
/// <param name="SummaryCode">Localization key for the one-line description.</param>
/// <param name="IconKey">Resource key of the geometry drawn beside it.</param>
/// <param name="Create">Makes a fresh rule of this type.</param>
public sealed record RuleDescriptor(
    string TypeKey,
    string NameCode,
    string SummaryCode,
    string IconKey,
    Func<IRenameRule> Create);

/// <summary>The rule types the UI offers, in the order they appear in the menu.</summary>
/// <remarks>
/// Ordered by how often people reach for them rather than alphabetically or by internal name: the
/// first three cover almost every rename anyone actually does.
/// </remarks>
public static class RuleCatalog
{
    /// <summary>Every rule type, in menu order.</summary>
    public static IReadOnlyList<RuleDescriptor> All { get; } =
    [
        new(PatternRule.Key, "rule.pattern.name", "rule.pattern.summary", "Icon.Pattern", () => new PatternRule { Pattern = "{name}" }),
        new(ReplaceRule.Key, "rule.replace.name", "rule.replace.summary", "Icon.Replace", () => new ReplaceRule()),
        new(NumberRule.Key, "rule.number.name", "rule.number.summary", "Icon.Number", () => new NumberRule()),
        new(InsertRule.Key, "rule.insert.name", "rule.insert.summary", "Icon.Insert", () => new InsertRule()),
        new(RemoveRule.Key, "rule.remove.name", "rule.remove.summary", "Icon.Remove", () => new RemoveRule()),
        new(CaseRule.Key, "rule.case.name", "rule.case.summary", "Icon.Case", () => new CaseRule()),
        new(ExtensionRule.Key, "rule.extension.name", "rule.extension.summary", "Icon.Extension", () => new ExtensionRule()),
        new(CleanupRule.Key, "rule.cleanup.name", "rule.cleanup.summary", "Icon.Cleanup", () => new CleanupRule())
    ];

    /// <summary>Finds the menu entry a rule instance came from.</summary>
    /// <param name="rule">Any rule, including one loaded from a preset.</param>
    /// <returns>The descriptor, or <see langword="null"/> for a rule type the UI does not know.</returns>
    public static RuleDescriptor? Describe(IRenameRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return All.FirstOrDefault(descriptor => descriptor.TypeKey == rule.TypeKey);
    }
}
