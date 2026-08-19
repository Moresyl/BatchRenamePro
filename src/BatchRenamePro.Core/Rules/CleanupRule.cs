using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Rules;

/// <summary>
/// Tidies a name: trims, collapses runs of whitespace, folds accents, swaps spaces for a separator,
/// strips characters Windows rejects and caps the length.
/// </summary>
/// <remarks>
/// Everything here is opt-in and the steps run in a fixed order — fold, substitute, strip, collapse,
/// trim, truncate — so the outcome does not depend on which checkbox the user ticked first.
/// Truncation happens last and is applied to the base name only, which is why the rule's default
/// scope leaves the extension out of it.
/// </remarks>
public sealed class CleanupRule : RenameRuleBase
{
    /// <summary>Preset discriminator. Part of the on-disk format.</summary>
    public const string Key = "cleanup";

    private bool _trimEnds = true;
    private bool _collapseWhitespace;
    private bool _replaceSpaces;
    private string _spaceReplacement = "_";
    private bool _removeDiacritics;
    private bool _stripInvalidCharacters = true;
    private string _invalidReplacement = string.Empty;
    private int _maxLength;
    private RenameScope _scope = RenameScope.BaseName;

    /// <inheritdoc />
    [JsonIgnore]
    public override string TypeKey => Key;

    /// <summary>Removes leading and trailing whitespace and dots.</summary>
    public bool TrimEnds
    {
        get => _trimEnds;
        set => Set(ref _trimEnds, value);
    }

    /// <summary>Collapses runs of whitespace into a single space.</summary>
    public bool CollapseWhitespace
    {
        get => _collapseWhitespace;
        set => Set(ref _collapseWhitespace, value);
    }

    /// <summary>Replaces spaces with <see cref="SpaceReplacement"/>.</summary>
    public bool ReplaceSpaces
    {
        get => _replaceSpaces;
        set => Set(ref _replaceSpaces, value);
    }

    /// <summary>What spaces become when <see cref="ReplaceSpaces"/> is on.</summary>
    public string SpaceReplacement
    {
        get => _spaceReplacement;
        set => Set(ref _spaceReplacement, value ?? string.Empty);
    }

    /// <summary>Folds accented letters to their base form, turning <c>café</c> into <c>cafe</c>.</summary>
    public bool RemoveDiacritics
    {
        get => _removeDiacritics;
        set => Set(ref _removeDiacritics, value);
    }

    /// <summary>Removes characters Windows does not allow in a file name.</summary>
    public bool StripInvalidCharacters
    {
        get => _stripInvalidCharacters;
        set => Set(ref _stripInvalidCharacters, value);
    }

    /// <summary>What an illegal character becomes; empty deletes it.</summary>
    public string InvalidReplacement
    {
        get => _invalidReplacement;
        set => Set(ref _invalidReplacement, value ?? string.Empty);
    }

    /// <summary>Maximum length in characters, or zero for no limit.</summary>
    public int MaxLength
    {
        get => _maxLength;
        set => Set(ref _maxLength, value);
    }

    /// <summary>Which part of the name is cleaned.</summary>
    public RenameScope Scope
    {
        get => _scope;
        set => Set(ref _scope, value);
    }

    /// <inheritdoc />
    public override NameParts Apply(NameParts input, RenameContext context) => _scope.Transform(input, Clean);

    /// <inheritdoc />
    public override IReadOnlyList<RuleDiagnostic> Validate()
    {
        var diagnostics = new List<RuleDiagnostic>();

        if (_maxLength < 0)
            diagnostics.Add(RuleDiagnostic.Error("rule.cleanup.maxLength", "The maximum length cannot be negative."));

        if (_replaceSpaces && _spaceReplacement.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            diagnostics.Add(RuleDiagnostic.Error("rule.cleanup.badReplacement", "The replacement contains characters Windows does not allow."));

        if (_stripInvalidCharacters && _invalidReplacement.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            diagnostics.Add(RuleDiagnostic.Error("rule.cleanup.badReplacement", "The replacement contains characters Windows does not allow."));

        return diagnostics;
    }

    /// <inheritdoc />
    public override IRenameRule Clone() => CopyBaseTo(new CleanupRule
    {
        _trimEnds = _trimEnds,
        _collapseWhitespace = _collapseWhitespace,
        _replaceSpaces = _replaceSpaces,
        _spaceReplacement = _spaceReplacement,
        _removeDiacritics = _removeDiacritics,
        _stripInvalidCharacters = _stripInvalidCharacters,
        _invalidReplacement = _invalidReplacement,
        _maxLength = _maxLength,
        _scope = _scope
    });

    /// <summary>Strips the combining marks left behind by Unicode decomposition.</summary>
    public static string FoldDiacritics(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) is not UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private string Clean(string value)
    {
        if (_removeDiacritics) value = FoldDiacritics(value);

        if (_replaceSpaces) value = value.Replace(" ", _spaceReplacement, StringComparison.Ordinal);

        if (_stripInvalidCharacters) value = StripInvalid(value);

        if (_collapseWhitespace) value = CollapseRuns(value);

        if (_trimEnds) value = value.Trim().Trim('.').Trim();

        if (_maxLength > 0 && value.Length > _maxLength)
        {
            value = value[.._maxLength];

            // Truncation must not resurrect a trailing space or dot, which Windows silently drops.
            if (_trimEnds) value = value.TrimEnd().TrimEnd('.').TrimEnd();
        }

        return value;
    }

    private string StripInvalid(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (Array.IndexOf(invalid, character) < 0) builder.Append(character);
            else builder.Append(_invalidReplacement);
        }

        return builder.ToString();
    }

    private static string CollapseRuns(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace) builder.Append(' ');
                previousWasSpace = true;
                continue;
            }

            builder.Append(character);
            previousWasSpace = false;
        }

        return builder.ToString();
    }
}
