using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Rules;

/// <summary>The capitalization <see cref="CaseRule"/> applies.</summary>
public enum CaseMode
{
    /// <summary>everything lowercase.</summary>
    Lower,

    /// <summary>EVERYTHING UPPERCASE.</summary>
    Upper,

    /// <summary>Every Word Capitalized.</summary>
    Title,

    /// <summary>First letter of the name only.</summary>
    Sentence,

    /// <summary>sWAPS tHE cASE oF eVERY lETTER.</summary>
    Invert
}

/// <summary>Changes the capitalization of a name.</summary>
/// <remarks>
/// Case conversion is invariant rather than culture-aware. That is deliberate: a file named
/// <c>title.txt</c> must produce the same result on a Turkish machine as on an English one, and the
/// dotted/dotless-i rules would otherwise silently change the bytes on disk.
/// </remarks>
public sealed class CaseRule : RenameRuleBase
{
    /// <summary>Preset discriminator. Part of the on-disk format.</summary>
    public const string Key = "case";

    private CaseMode _mode = CaseMode.Lower;
    private RenameScope _scope = RenameScope.BaseName;

    /// <inheritdoc />
    [JsonIgnore]
    public override string TypeKey => Key;

    /// <summary>The capitalization to apply.</summary>
    public CaseMode Mode
    {
        get => _mode;
        set => Set(ref _mode, value);
    }

    /// <summary>Which part of the name is affected.</summary>
    public RenameScope Scope
    {
        get => _scope;
        set => Set(ref _scope, value);
    }

    /// <inheritdoc />
    public override NameParts Apply(NameParts input, RenameContext context) => _scope.Transform(input, context, Convert);

    /// <inheritdoc />
    public override IRenameRule Clone() => CopyBaseTo(new CaseRule { _mode = _mode, _scope = _scope });

    private string Convert(string value) => _mode switch
    {
        CaseMode.Lower => value.ToLowerInvariant(),
        CaseMode.Upper => value.ToUpperInvariant(),
        CaseMode.Title => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant()),
        CaseMode.Sentence => ToSentenceCase(value),
        CaseMode.Invert => Invert(value),
        _ => value
    };

    private static string ToSentenceCase(string value)
    {
        if (value.Length == 0) return value;

        var lowered = value.ToLowerInvariant();
        var first = lowered.AsSpan().IndexOfAnyExcept(' ', '\t');
        if (first < 0) return lowered;

        return string.Concat(lowered.AsSpan(0, first), char.ToUpperInvariant(lowered[first]).ToString(), lowered.AsSpan(first + 1));
    }

    private static string Invert(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsUpper(character)
                ? char.ToLowerInvariant(character)
                : char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
