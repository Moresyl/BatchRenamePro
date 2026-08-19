using System.Globalization;
using System.Text;
using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Tokens;

/// <summary>A token the user can insert into a pattern, surfaced by the UI's token picker.</summary>
/// <param name="Token">The literal text inserted into the pattern, for example <c>{modified:yyyy-MM-dd}</c>.</param>
/// <param name="DescriptionCode">Localization key for the token's description.</param>
/// <param name="Category">Grouping key for the picker.</param>
public sealed record TokenDescriptor(string Token, string DescriptionCode, string Category);

/// <summary>
/// Expands a naming pattern into a concrete file name.
/// </summary>
/// <remarks>
/// <para>
/// Expansion is a single left-to-right scan rather than a series of string replacements. That
/// matters for correctness: a file literally named <c>report#2</c> must not have its <c>#</c>
/// re-interpreted as a sequence token after <c>{name}</c> has been substituted. Substituted values
/// are written straight to the output buffer and never rescanned.
/// </para>
/// <para>
/// Unknown tokens are emitted verbatim so a typo shows up in the preview instead of silently
/// deleting part of the name; <see cref="FindUnknownTokens"/> lets a rule turn that into a warning.
/// </para>
/// </remarks>
public static class TokenEngine
{
    private const string RandomAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Every token the picker offers, in display order.</summary>
    public static IReadOnlyList<TokenDescriptor> Catalog { get; } =
    [
        new("{name}", "token.name", "text"),
        new("{ext}", "token.ext", "text"),
        new("{original}", "token.original", "text"),
        new("{parent}", "token.parent", "text"),
        new("{name:upper}", "token.nameUpper", "text"),
        new("{name:lower}", "token.nameLower", "text"),
        new("{name:title}", "token.nameTitle", "text"),
        new("{index}", "token.index", "number"),
        new("{index:000}", "token.indexPadded", "number"),
        new("{total}", "token.total", "number"),
        new("{size}", "token.size", "number"),
        new("{size:MB}", "token.sizeMb", "number"),
        new("{date}", "token.date", "time"),
        new("{time}", "token.time", "time"),
        new("{date:yyyy-MM-dd}", "token.dateFormat", "time"),
        new("{created:yyyy-MM-dd}", "token.created", "time"),
        new("{modified:yyyy-MM-dd}", "token.modified", "time"),
        new("{year}", "token.year", "time"),
        new("{month}", "token.month", "time"),
        new("{day}", "token.day", "time"),
        new("{guid}", "token.guid", "unique"),
        new("{rand:6}", "token.rand", "unique")
    ];

    private static readonly HashSet<string> KnownTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "n", "ext", "extension", "fullname", "original", "originalname", "parent", "folder",
        "index", "i", "counter", "num", "total",
        "date", "time", "datetime", "year", "month", "day", "hour", "minute", "second",
        "created", "modified", "size", "guid", "rand", "random"
    };

    /// <summary>Expands a pattern for one item.</summary>
    /// <param name="pattern">The pattern text, which may contain tokens, <c>*</c> and <c>#</c>.</param>
    /// <param name="current">
    /// The name as the pipeline has it so far. <c>{name}</c> resolves against this, not against the
    /// name on disk, so a pattern rule composes with the rules that ran before it. Use
    /// <c>{original}</c> to reach past them.
    /// </param>
    /// <param name="context">The item being renamed.</param>
    /// <param name="sequence">Numbering settings used by sequence tokens.</param>
    public static string Expand(string pattern, NameParts current, RenameContext context, SequenceOptions sequence)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(pattern)) return string.Empty;

        var parts = current;
        var output = new StringBuilder(pattern.Length + 32);

        for (var i = 0; i < pattern.Length; i++)
        {
            var character = pattern[i];

            switch (character)
            {
                // {{ and }} escape a literal brace.
                case '{' when i + 1 < pattern.Length && pattern[i + 1] == '{':
                case '}' when i + 1 < pattern.Length && pattern[i + 1] == '}':
                    output.Append(character);
                    i++;
                    break;

                case '{':
                    {
                        var close = pattern.IndexOf('}', i + 1);
                        if (close < 0)
                        {
                            // Unterminated brace: emit the rest literally.
                            output.Append(pattern, i, pattern.Length - i);
                            i = pattern.Length;
                            break;
                        }

                        var body = pattern[(i + 1)..close];
                        output.Append(ExpandToken(body, context, parts, sequence));
                        i = close;
                        break;
                    }

                // Legacy pattern syntax kept for muscle memory from classic Windows rename tools.
                case '*':
                    output.Append(parts.BaseName);
                    break;

                case '#':
                    {
                        var run = 1;
                        while (i + run < pattern.Length && pattern[i + run] == '#') run++;
                        output.Append(SequenceFormatter.Format(context.Index, sequence, run > 1 ? run : null));
                        i += run - 1;
                        break;
                    }

                default:
                    output.Append(character);
                    break;
            }
        }

        return output.ToString();
    }

    /// <summary>Returns the names of any tokens in <paramref name="pattern"/> that the engine does not recognise.</summary>
    public static IReadOnlyList<string> FindUnknownTokens(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return [];

        var unknown = new List<string>();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '{') continue;
            if (i + 1 < pattern.Length && pattern[i + 1] == '{')
            {
                i++;
                continue;
            }

            var close = pattern.IndexOf('}', i + 1);
            if (close < 0) break;

            var body = pattern[(i + 1)..close];
            var colon = body.IndexOf(':', StringComparison.Ordinal);
            var name = colon >= 0 ? body[..colon] : body;
            if (name.Length > 0 && !KnownTokens.Contains(name) && !unknown.Contains(name, StringComparer.OrdinalIgnoreCase))
                unknown.Add(name);

            i = close;
        }

        return unknown;
    }

    private static string ExpandToken(string body, RenameContext context, NameParts parts, SequenceOptions sequence)
    {
        var colon = body.IndexOf(':', StringComparison.Ordinal);
        var name = (colon >= 0 ? body[..colon] : body).Trim();
        var format = colon >= 0 ? body[(colon + 1)..] : string.Empty;
        var timestamp = context.Timestamp;

        return name.ToLowerInvariant() switch
        {
            "name" or "n" => ApplyTextFormat(parts.BaseName, format),
            "ext" or "extension" => ApplyTextFormat(parts.Extension.TrimStart('.'), format),
            "fullname" => ApplyTextFormat(parts.FullName, format),
            "original" => ApplyTextFormat(context.OriginalParts.BaseName, format),
            "originalname" => ApplyTextFormat(context.OriginalName, format),
            "parent" or "folder" => ApplyTextFormat(context.ParentName, format),

            "index" or "i" or "counter" or "num" => FormatSequence(context.Index, sequence, format),
            "total" => context.Total.ToString(CultureInfo.InvariantCulture),

            "date" => FormatTime(timestamp, format, "yyyyMMdd"),
            "time" => FormatTime(timestamp, format, "HHmmss"),
            "datetime" => FormatTime(timestamp, format, "yyyyMMdd-HHmmss"),
            "year" => FormatTime(timestamp, format, "yyyy"),
            "month" => FormatTime(timestamp, format, "MM"),
            "day" => FormatTime(timestamp, format, "dd"),
            "hour" => FormatTime(timestamp, format, "HH"),
            "minute" => FormatTime(timestamp, format, "mm"),
            "second" => FormatTime(timestamp, format, "ss"),

            "created" => FormatTime(context.Facts.CreatedAt, format, "yyyyMMdd"),
            "modified" => FormatTime(context.Facts.ModifiedAt, format, "yyyyMMdd"),
            "size" => FormatSize(context.Facts.SizeInBytes, format),

            "guid" => FormatGuid(context, format),
            "rand" or "random" => FormatRandom(context, format),

            // Unknown: hand the text back untouched so the user can see their typo in the preview.
            _ => "{" + body + "}"
        };
    }

    private static string ApplyTextFormat(string value, string format) => format.ToLowerInvariant() switch
    {
        "upper" or "u" => value.ToUpperInvariant(),
        "lower" or "l" => value.ToLowerInvariant(),
        "title" or "t" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant()),
        _ => value
    };

    private static string FormatSequence(int index, SequenceOptions sequence, string format)
    {
        // A format of "000" means "pad to three digits" without disturbing the batch's numbering style.
        if (format.Length > 0 && format.All(character => character == '0'))
            return SequenceFormatter.Format(index, sequence, format.Length);

        return SequenceFormatter.Format(index, sequence);
    }

    private static string FormatTime(DateTimeOffset value, string format, string fallback)
    {
        if (value == default) return string.Empty;

        try
        {
            return value.ToString(format.Length > 0 ? format : fallback, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value.ToString(fallback, CultureInfo.InvariantCulture);
        }
    }

    private static string FormatSize(long bytes, string format) => format.ToLowerInvariant() switch
    {
        "kb" => (bytes / 1024).ToString(CultureInfo.InvariantCulture),
        "mb" => (bytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture),
        "gb" => (bytes / (1024L * 1024 * 1024)).ToString(CultureInfo.InvariantCulture),
        "auto" => FormatSizeCompact(bytes),
        _ => bytes.ToString(CultureInfo.InvariantCulture)
    };

    private static string FormatSizeCompact(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => FormattableString.Invariant($"{bytes / (double)(1024L * 1024 * 1024):0.#}GB"),
        >= 1024 * 1024 => FormattableString.Invariant($"{bytes / (double)(1024 * 1024):0.#}MB"),
        >= 1024 => FormattableString.Invariant($"{bytes / 1024.0:0.#}KB"),
        _ => FormattableString.Invariant($"{bytes}B")
    };

    private static string FormatGuid(RenameContext context, string format)
    {
        // Derived from the batch timestamp and item index so the preview does not churn on every keystroke.
        var guid = DeterministicGuid(context);
        var specifier = format.Length == 1 ? format : "N";

        try
        {
            return guid.ToString(specifier, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return guid.ToString("N", CultureInfo.InvariantCulture);
        }
    }

    private static string FormatRandom(RenameContext context, string format)
    {
        var length = int.TryParse(format, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 1, 32)
            : 6;

        var random = new Random(HashCode.Combine(context.Timestamp.Ticks, context.Index));
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++) builder.Append(RandomAlphabet[random.Next(RandomAlphabet.Length)]);
        return builder.ToString();
    }

    private static Guid DeterministicGuid(RenameContext context)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, context.Timestamp.Ticks);
        BitConverter.TryWriteBytes(bytes[8..], (long)HashCode.Combine(context.Index, context.OriginalName));
        return new Guid(bytes);
    }
}
