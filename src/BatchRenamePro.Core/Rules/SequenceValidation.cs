using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Rules;

/// <summary>Validation shared by every rule that generates numbering.</summary>
internal static class SequenceValidation
{
    /// <summary>Returns every problem with a numbering configuration.</summary>
    public static IEnumerable<RuleDiagnostic> Validate(SequenceSettings sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        if (sequence.Step == 0)
            yield return RuleDiagnostic.Error("rule.sequence.zeroStep", "The numbering increment cannot be zero.");

        if (sequence.Padding is < SequenceOptions.MinPadding or > SequenceOptions.MaxPadding)
        {
            yield return RuleDiagnostic.Error(
                "rule.sequence.padding",
                $"Digit width must be between {SequenceOptions.MinPadding} and {SequenceOptions.MaxPadding}.");
        }

        if (sequence.GroupSize < 1)
            yield return RuleDiagnostic.Error("rule.sequence.groupSize", "Items per number must be at least 1.");

        if (sequence.Style is SequenceStyle.Alphabetic && sequence.Start < 1)
            yield return RuleDiagnostic.Warning("rule.sequence.alphabeticStart", "Letter numbering starts at 1 (A); lower values fall back to digits.");

        if (sequence.Style is SequenceStyle.Roman && sequence.Start < 1)
            yield return RuleDiagnostic.Warning("rule.sequence.romanStart", "Roman numerals start at 1 (I); lower values fall back to digits.");
    }
}
