using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Rules;

/// <summary>Finds text and replaces it, either literally or with a .NET regular expression.</summary>
/// <remarks>
/// Regular expressions run with a one second match timeout so a catastrophically backtracking
/// pattern typed into the live preview cannot freeze the window. The compiled regex is cached and
/// rebuilt only when the pattern or its options change, because the preview re-runs on every
/// keystroke across the whole selection.
/// </remarks>
public sealed class ReplaceRule : RenameRuleBase
{
    /// <summary>Preset discriminator. Part of the on-disk format.</summary>
    public const string Key = "replace";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private string _find = string.Empty;
    private string _replaceWith = string.Empty;
    private bool _useRegex;
    private bool _ignoreCase = true;
    private bool _firstOccurrenceOnly;
    private RenameScope _scope = RenameScope.BaseName;

    private Regex? _cachedRegex;
    private string? _cachedPattern;
    private RegexOptions _cachedOptions;

    /// <inheritdoc />
    [JsonIgnore]
    public override string TypeKey => Key;

    /// <summary>The text or regular expression to search for.</summary>
    public string Find
    {
        get => _find;
        set
        {
            if (Set(ref _find, value ?? string.Empty)) _cachedRegex = null;
        }
    }

    /// <summary>The replacement text. Supports <c>$1</c> style group references when <see cref="UseRegex"/> is set.</summary>
    public string ReplaceWith
    {
        get => _replaceWith;
        set => Set(ref _replaceWith, value ?? string.Empty);
    }

    /// <summary>Whether <see cref="Find"/> is a regular expression.</summary>
    public bool UseRegex
    {
        get => _useRegex;
        set
        {
            if (Set(ref _useRegex, value)) _cachedRegex = null;
        }
    }

    /// <summary>Whether matching ignores case.</summary>
    public bool IgnoreCase
    {
        get => _ignoreCase;
        set
        {
            if (Set(ref _ignoreCase, value)) _cachedRegex = null;
        }
    }

    /// <summary>Whether only the first match is replaced.</summary>
    public bool FirstOccurrenceOnly
    {
        get => _firstOccurrenceOnly;
        set => Set(ref _firstOccurrenceOnly, value);
    }

    /// <summary>Which part of the name is searched.</summary>
    public RenameScope Scope
    {
        get => _scope;
        set => Set(ref _scope, value);
    }

    /// <inheritdoc />
    public override NameParts Apply(NameParts input, RenameContext context)
    {
        if (_find.Length == 0) return input;
        return _scope.Transform(input, context, Replace);
    }

    /// <inheritdoc />
    public override IReadOnlyList<RuleDiagnostic> Validate()
    {
        if (_find.Length == 0)
            return [RuleDiagnostic.Error("rule.replace.emptyFind", "Enter the text to search for.")];

        if (!_useRegex) return [];

        try
        {
            _ = GetRegex();
        }
        catch (ArgumentException error)
        {
            return [RuleDiagnostic.Error("rule.replace.badRegex", $"Invalid regular expression: {error.Message}")];
        }

        return [];
    }

    /// <inheritdoc />
    public override IRenameRule Clone() => CopyBaseTo(new ReplaceRule
    {
        _find = _find,
        _replaceWith = _replaceWith,
        _useRegex = _useRegex,
        _ignoreCase = _ignoreCase,
        _firstOccurrenceOnly = _firstOccurrenceOnly,
        _scope = _scope
    });

    private string Replace(string value)
    {
        if (!_useRegex)
        {
            var comparison = _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (!_firstOccurrenceOnly) return value.Replace(_find, _replaceWith, comparison);

            var at = value.IndexOf(_find, comparison);
            return at < 0 ? value : string.Concat(value.AsSpan(0, at), _replaceWith, value.AsSpan(at + _find.Length));
        }

        try
        {
            var regex = GetRegex();
            return _firstOccurrenceOnly
                ? regex.Replace(value, _replaceWith, 1)
                : regex.Replace(value, _replaceWith);
        }
        catch (Exception error) when (error is ArgumentException or RegexMatchTimeoutException)
        {
            // A broken or runaway expression leaves the name untouched; Validate() surfaces the reason.
            return value;
        }
    }

    private Regex GetRegex()
    {
        var options = RegexOptions.CultureInvariant | (_ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);

        if (_cachedRegex is not null && _cachedPattern == _find && _cachedOptions == options) return _cachedRegex;

        var regex = new Regex(_find, options, MatchTimeout);
        _cachedRegex = regex;
        _cachedPattern = _find;
        _cachedOptions = options;
        return regex;
    }
}
