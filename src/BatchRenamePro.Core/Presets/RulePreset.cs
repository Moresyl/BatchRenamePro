using System.Text.Json.Serialization;
using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Planning;

namespace BatchRenamePro.Core.Presets;

/// <summary>A named, reusable rule pipeline.</summary>
/// <param name="Name">Display name, unique within a store.</param>
/// <param name="Rules">The pipeline, in execution order.</param>
/// <param name="ConflictPolicy">The collision policy saved with the preset.</param>
/// <param name="DescriptionCode">
/// Localization key for built-in presets. User presets leave this empty and use
/// <paramref name="Description"/> instead.
/// </param>
/// <param name="Description">Free-text description for user-authored presets.</param>
/// <param name="IsBuiltIn">Whether the preset ships with the app and cannot be overwritten.</param>
public sealed record RulePreset(
    string Name,
    IReadOnlyList<RenameRuleBase> Rules,
    ConflictPolicy ConflictPolicy = ConflictPolicy.Block,
    string DescriptionCode = "",
    string Description = "",
    bool IsBuiltIn = false)
{
    /// <summary>The schema version of the saved format, so future versions can migrate old files.</summary>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Creates a deep copy, so applying a preset never hands out the stored rule instances.</summary>
    /// <remarks>Named <c>DeepCopy</c> because records reserve <c>Clone</c> for the compiler-generated copy.</remarks>
    public RulePreset DeepCopy() => this with
    {
        Rules = [.. Rules.Select(rule => (RenameRuleBase)rule.Clone())]
    };
}
