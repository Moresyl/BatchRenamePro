using System.Text.Json.Serialization;
using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Rules;

/// <summary>What <see cref="ExtensionRule"/> does to an extension.</summary>
public enum ExtensionMode
{
    /// <summary>Leave it alone.</summary>
    Keep,

    /// <summary>Convert to lowercase — the usual fix for <c>.JPG</c> from a camera.</summary>
    Lower,

    /// <summary>Convert to uppercase.</summary>
    Upper,

    /// <summary>Replace it with <see cref="ExtensionRule.NewExtension"/>.</summary>
    Replace,

    /// <summary>Remove it entirely.</summary>
    Remove,

    /// <summary>Add <see cref="ExtensionRule.NewExtension"/> only when the item has none.</summary>
    AddIfMissing
}

/// <summary>Normalizes or replaces a file extension. Directories are never touched.</summary>
public sealed class ExtensionRule : RenameRuleBase
{
    /// <summary>Preset discriminator. Part of the on-disk format.</summary>
    public const string Key = "extension";

    private ExtensionMode _mode = ExtensionMode.Lower;
    private string _newExtension = string.Empty;

    /// <inheritdoc />
    [JsonIgnore]
    public override string TypeKey => Key;

    /// <summary>What to do with the extension.</summary>
    public ExtensionMode Mode
    {
        get => _mode;
        set => Set(ref _mode, value);
    }

    /// <summary>The replacement extension, with or without a leading dot.</summary>
    public string NewExtension
    {
        get => _newExtension;
        set => Set(ref _newExtension, value ?? string.Empty);
    }

    /// <inheritdoc />
    public override NameParts Apply(NameParts input, RenameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A folder called "archive.2024" has no extension to normalize.
        if (context.IsDirectory) return input;

        return _mode switch
        {
            ExtensionMode.Lower => input with { Extension = input.Extension.ToLowerInvariant() },
            ExtensionMode.Upper => input with { Extension = input.Extension.ToUpperInvariant() },
            ExtensionMode.Replace => input with { Extension = Normalize(_newExtension) },
            ExtensionMode.Remove => input with { Extension = string.Empty },
            ExtensionMode.AddIfMissing => input.Extension.Length > 0 ? input : input with { Extension = Normalize(_newExtension) },
            _ => input
        };
    }

    /// <inheritdoc />
    public override IReadOnlyList<RuleDiagnostic> Validate()
    {
        if (_mode is not (ExtensionMode.Replace or ExtensionMode.AddIfMissing)) return [];

        var value = _newExtension.Trim().TrimStart('.');

        if (value.Length == 0)
            return [RuleDiagnostic.Error("rule.extension.empty", "Enter the new extension.")];

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return [RuleDiagnostic.Error("rule.extension.invalidChars", "The extension contains characters Windows does not allow.")];

        return [];
    }

    /// <inheritdoc />
    public override IRenameRule Clone() => CopyBaseTo(new ExtensionRule { _mode = _mode, _newExtension = _newExtension });

    private static string Normalize(string extension)
    {
        var value = extension.Trim().TrimStart('.');
        return value.Length == 0 ? string.Empty : "." + value;
    }
}
