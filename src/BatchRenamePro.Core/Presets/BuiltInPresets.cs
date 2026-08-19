using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Planning;
using BatchRenamePro.Core.Rules;
using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Presets;

/// <summary>
/// Ready-made pipelines for the jobs people actually open a rename tool to do.
/// </summary>
/// <remarks>
/// These double as worked examples of how rules compose, which is why each one uses more than a
/// single rule where the real task needs more than a single rule.
/// </remarks>
public static class BuiltInPresets
{
    /// <summary>Every built-in preset, in display order.</summary>
    public static IReadOnlyList<RulePreset> All { get; } =
    [
        Photos(),
        SequentialNumbering(),
        WebSafe(),
        DatePrefix(),
        TidyDownloads(),
        LowercaseExtensions()
    ];

    /// <summary>Looks a built-in preset up by name.</summary>
    public static RulePreset? Find(string name) =>
        All.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary><c>2026-08-19_Holiday_001.jpg</c> — date taken, a label, then a counter.</summary>
    private static RulePreset Photos() => new(
        "preset.photos",
        [
            new PatternRule { Pattern = "{modified:yyyy-MM-dd}_{name}", Scope = RenameScope.BaseName },
            new NumberRule
            {
                Position = InsertPosition.Suffix,
                Separator = "_",
                Sequence = new SequenceSettings { Start = 1, Padding = 3 }
            },
            new ExtensionRule { Mode = ExtensionMode.Lower }
        ],
        ConflictPolicy.AutoNumber,
        DescriptionCode: "preset.photos.description",
        IsBuiltIn: true);

    /// <summary><c>01 - original name.ext</c> — plain sequential numbering that keeps the name.</summary>
    private static RulePreset SequentialNumbering() => new(
        "preset.numbering",
        [
            new NumberRule
            {
                Position = InsertPosition.Prefix,
                Separator = " - ",
                Sequence = new SequenceSettings { Start = 1, Padding = 2 }
            }
        ],
        ConflictPolicy.AutoNumber,
        DescriptionCode: "preset.numbering.description",
        IsBuiltIn: true);

    /// <summary><c>my-file-name.pdf</c> — lowercase, no accents, no spaces, no punctuation surprises.</summary>
    private static RulePreset WebSafe() => new(
        "preset.webSafe",
        [
            new CleanupRule
            {
                RemoveDiacritics = true,
                CollapseWhitespace = true,
                ReplaceSpaces = true,
                SpaceReplacement = "-",
                StripInvalidCharacters = true,
                TrimEnds = true
            },
            new RemoveRule { Mode = RemoveMode.Symbols, KeepCharacters = "-_" },
            new CaseRule { Mode = CaseMode.Lower },
            new ExtensionRule { Mode = ExtensionMode.Lower }
        ],
        ConflictPolicy.AutoNumber,
        DescriptionCode: "preset.webSafe.description",
        IsBuiltIn: true);

    /// <summary><c>2026-08-19 report.docx</c> — sortable date in front of the existing name.</summary>
    private static RulePreset DatePrefix() => new(
        "preset.datePrefix",
        [
            new InsertRule
            {
                Text = "{modified:yyyy-MM-dd} ",
                Position = InsertPosition.Prefix
            }
        ],
        ConflictPolicy.AutoNumber,
        DescriptionCode: "preset.datePrefix.description",
        IsBuiltIn: true);

    /// <summary>Strips the <c>(1)</c>, <c>-copy</c> and duplicate-download noise off a folder of files.</summary>
    private static RulePreset TidyDownloads() => new(
        "preset.tidy",
        [
            new ReplaceRule
            {
                Find = @"[ _-]*\((\d+)\)$",
                ReplaceWith = "",
                UseRegex = true,
                Scope = RenameScope.BaseName
            },
            new ReplaceRule
            {
                Find = @"[ _-]*(copy|副本|- Copy)$",
                ReplaceWith = "",
                UseRegex = true,
                IgnoreCase = true,
                Scope = RenameScope.BaseName
            },
            new CleanupRule { CollapseWhitespace = true, TrimEnds = true }
        ],
        ConflictPolicy.AutoNumber,
        DescriptionCode: "preset.tidy.description",
        IsBuiltIn: true);

    /// <summary><c>.JPG</c> becomes <c>.jpg</c> and nothing else changes.</summary>
    private static RulePreset LowercaseExtensions() => new(
        "preset.lowerExt",
        [
            new ExtensionRule { Mode = ExtensionMode.Lower }
        ],
        ConflictPolicy.Block,
        DescriptionCode: "preset.lowerExt.description",
        IsBuiltIn: true);
}
