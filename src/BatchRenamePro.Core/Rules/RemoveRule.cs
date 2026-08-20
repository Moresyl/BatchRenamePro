using System.Text.Json.Serialization;
using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Rules;

/// <summary>What <see cref="RemoveRule"/> takes out of a name.</summary>
public enum RemoveMode
{
    /// <summary>The first N characters.</summary>
    FromStart,

    /// <summary>The last N characters.</summary>
    FromEnd,

    /// <summary>N characters starting at an offset.</summary>
    Range,

    /// <summary>Every occurrence of a literal string.</summary>
    Text,

    /// <summary>Every digit.</summary>
    Digits,

    /// <summary>Everything except letters, digits and the characters in <see cref="RemoveRule.KeepCharacters"/>.</summary>
    Symbols,

    /// <summary>Everything from the first occurrence of a marker onwards, marker included.</summary>
    AfterMarker,

    /// <summary>Everything up to and including the last occurrence of a marker.</summary>
    BeforeMarker
}

/// <summary>Deletes characters from a name by offset, by literal text or by character class.</summary>
public sealed class RemoveRule : RenameRuleBase
{
    /// <summary>Preset discriminator. Part of the on-disk format.</summary>
    public const string Key = "remove";

    private RemoveMode _mode = RemoveMode.FromStart;
    private int _count = 1;
    private int _position;
    private string _text = string.Empty;
    private string _keepCharacters = " -_.";
    private bool _ignoreCase = true;
    private RenameScope _scope = RenameScope.BaseName;

    /// <inheritdoc />
    [JsonIgnore]
    public override string TypeKey => Key;

    /// <summary>What to remove.</summary>
    public RemoveMode Mode
    {
        get => _mode;
        set => Set(ref _mode, value);
    }

    /// <summary>How many characters to remove, for the offset-based modes.</summary>
    public int Count
    {
        get => _count;
        set => Set(ref _count, value);
    }

    /// <summary>Where removal starts, for <see cref="RemoveMode.Range"/>. Zero-based, like <see cref="string.Remove(int, int)"/>.</summary>
    /// <remarks>The UI presents this one-based, because that is how people count characters.</remarks>
    public int Position
    {
        get => _position;
        set => Set(ref _position, value);
    }

    /// <summary>The literal string or marker, for the text-based modes.</summary>
    public string Text
    {
        get => _text;
        set => Set(ref _text, value ?? string.Empty);
    }

    /// <summary>Punctuation preserved by <see cref="RemoveMode.Symbols"/>.</summary>
    public string KeepCharacters
    {
        get => _keepCharacters;
        set => Set(ref _keepCharacters, value ?? string.Empty);
    }

    /// <summary>Whether text matching ignores case.</summary>
    public bool IgnoreCase
    {
        get => _ignoreCase;
        set => Set(ref _ignoreCase, value);
    }

    /// <summary>Which part of the name is affected.</summary>
    public RenameScope Scope
    {
        get => _scope;
        set => Set(ref _scope, value);
    }

    /// <inheritdoc />
    public override NameParts Apply(NameParts input, RenameContext context) => _scope.Transform(input, context, Remove);

    /// <inheritdoc />
    public override IReadOnlyList<RuleDiagnostic> Validate()
    {
        var diagnostics = new List<RuleDiagnostic>();

        switch (_mode)
        {
            case RemoveMode.FromStart or RemoveMode.FromEnd or RemoveMode.Range when _count < 1:
                diagnostics.Add(RuleDiagnostic.Error("rule.remove.count", "The number of characters to remove must be at least 1."));
                break;

            case RemoveMode.Text or RemoveMode.AfterMarker or RemoveMode.BeforeMarker when _text.Length == 0:
                diagnostics.Add(RuleDiagnostic.Error("rule.remove.emptyText", "Enter the text to remove."));
                break;
        }

        if (_mode is RemoveMode.Range && _position < 0)
            diagnostics.Add(RuleDiagnostic.Error("rule.remove.negativePosition", "The start position cannot be negative."));

        return diagnostics;
    }

    /// <inheritdoc />
    public override IRenameRule Clone() => CopyBaseTo(new RemoveRule
    {
        _mode = _mode,
        _count = _count,
        _position = _position,
        _text = _text,
        _keepCharacters = _keepCharacters,
        _ignoreCase = _ignoreCase,
        _scope = _scope
    });

    private string Remove(string value)
    {
        if (value.Length == 0) return value;
        var comparison = _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        switch (_mode)
        {
            case RemoveMode.FromStart:
                return value[Math.Min(Math.Max(_count, 0), value.Length)..];

            case RemoveMode.FromEnd:
                return value[..Math.Max(0, value.Length - Math.Max(_count, 0))];

            case RemoveMode.Range:
                {
                    if (_position < 0 || _position >= value.Length) return value;
                    return value.Remove(_position, Math.Min(Math.Max(_count, 0), value.Length - _position));
                }

            case RemoveMode.Text:
                return _text.Length == 0 ? value : value.Replace(_text, string.Empty, comparison);

            case RemoveMode.Digits:
                return new string([.. value.Where(character => !char.IsDigit(character))]);

            case RemoveMode.Symbols:
                return new string([.. value.Where(character => char.IsLetterOrDigit(character) || _keepCharacters.Contains(character, StringComparison.Ordinal))]);

            case RemoveMode.AfterMarker:
                {
                    if (_text.Length == 0) return value;
                    var at = value.IndexOf(_text, comparison);
                    return at < 0 ? value : value[..at];
                }

            case RemoveMode.BeforeMarker:
                {
                    if (_text.Length == 0) return value;
                    var at = value.LastIndexOf(_text, comparison);
                    return at < 0 ? value : value[(at + _text.Length)..];
                }

            default:
                return value;
        }
    }
}
