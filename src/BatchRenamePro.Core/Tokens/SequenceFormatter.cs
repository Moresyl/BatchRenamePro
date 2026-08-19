using System.Globalization;
using System.Text;

namespace BatchRenamePro.Core.Tokens;

/// <summary>The alphabet a generated sequence counts in.</summary>
public enum SequenceStyle
{
    /// <summary>1, 2, 3 … zero-padded to a fixed width.</summary>
    Numeric,

    /// <summary>A, B, C … Z, AA, AB — spreadsheet-column style.</summary>
    Alphabetic,

    /// <summary>I, II, III, IV — uppercase Roman numerals.</summary>
    Roman,

    /// <summary>0, 1, … 9, a, b — base 36, useful for very dense numbering.</summary>
    Base36
}

/// <summary>How a batch position is turned into the text substituted for a sequence token.</summary>
/// <param name="Start">Value assigned to the first item.</param>
/// <param name="Step">Amount added for each subsequent item. May be negative but never zero.</param>
/// <param name="Padding">Minimum width for <see cref="SequenceStyle.Numeric"/> and <see cref="SequenceStyle.Base36"/>.</param>
/// <param name="Style">The alphabet to count in.</param>
/// <param name="GroupSize">
/// When greater than one, consecutive items share a number and it advances every N items — used for
/// numbering photo pairs, chapter parts and similar groupings.
/// </param>
public readonly record struct SequenceOptions(
    int Start = 1,
    int Step = 1,
    int Padding = 2,
    SequenceStyle Style = SequenceStyle.Numeric,
    int GroupSize = 1)
{
    /// <summary>The smallest padding the UI and engine accept.</summary>
    public const int MinPadding = 1;

    /// <summary>The largest padding the UI and engine accept.</summary>
    public const int MaxPadding = 12;

    /// <summary>Default numbering: starts at 1, steps by 1, two digits wide.</summary>
    /// <remarks>
    /// Spelled out rather than written <c>new()</c>: a record struct's parameterless constructor
    /// zero-initialises instead of applying the primary-constructor defaults, which would silently
    /// produce a step of 0 and no padding. For the same reason, never write
    /// <c>default(SequenceOptions)</c> — ask for this instead.
    /// </remarks>
    public static SequenceOptions Default { get; } = new(Start: 1, Step: 1, Padding: 2, Style: SequenceStyle.Numeric, GroupSize: 1);

    /// <summary>Computes the raw sequence value for a zero-based batch position.</summary>
    /// <param name="index">Zero-based position within the batch.</param>
    public long ValueAt(int index)
    {
        var group = GroupSize > 1 ? index / GroupSize : index;
        return Start + (long)group * Step;
    }
}

/// <summary>Renders sequence values in the styles offered by <see cref="SequenceStyle"/>.</summary>
public static class SequenceFormatter
{
    private const string Base36Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

    private static readonly (int Value, string Symbol)[] RomanNumerals =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
    ];

    /// <summary>Formats the sequence value for a batch position.</summary>
    /// <param name="index">Zero-based position within the batch.</param>
    /// <param name="options">Numbering settings.</param>
    /// <param name="paddingOverride">Overrides <see cref="SequenceOptions.Padding"/> when supplied, for <c>###</c> style tokens.</param>
    public static string Format(int index, SequenceOptions options, int? paddingOverride = null)
    {
        var value = options.ValueAt(index);
        var padding = Math.Clamp(paddingOverride ?? options.Padding, SequenceOptions.MinPadding, SequenceOptions.MaxPadding);

        return options.Style switch
        {
            SequenceStyle.Numeric => FormatDecimal(value, padding),
            SequenceStyle.Alphabetic => FormatAlphabetic(value),
            SequenceStyle.Roman => FormatRoman(value),
            SequenceStyle.Base36 => FormatBase36(value, padding),
            _ => FormatDecimal(value, padding)
        };
    }

    /// <summary>
    /// Formats a decimal value, padded to at least <paramref name="padding"/> digits.
    /// </summary>
    /// <remarks>
    /// Padding is a minimum, not a fixed width: once a batch passes 99 with two-digit padding the
    /// numbering continues 100, 101 rather than wrapping or truncating.
    /// </remarks>
    private static string FormatDecimal(long value, int padding)
    {
        var sign = value < 0 ? "-" : string.Empty;
        var digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        return sign + digits.PadLeft(padding, '0');
    }

    /// <summary>Formats a value as a spreadsheet-style column label. Values below 1 fall back to decimal.</summary>
    private static string FormatAlphabetic(long value)
    {
        if (value < 1) return FormatDecimal(value, 1);

        var builder = new StringBuilder();
        var remaining = value;
        while (remaining > 0)
        {
            remaining--;
            builder.Insert(0, (char)('A' + (int)(remaining % 26)));
            remaining /= 26;
        }

        return builder.ToString();
    }

    /// <summary>Formats a value as a Roman numeral. Values outside 1-3999 fall back to decimal.</summary>
    private static string FormatRoman(long value)
    {
        if (value is < 1 or > 3999) return FormatDecimal(value, 1);

        var builder = new StringBuilder();
        var remaining = (int)value;
        foreach (var (numeral, symbol) in RomanNumerals)
        {
            while (remaining >= numeral)
            {
                builder.Append(symbol);
                remaining -= numeral;
            }
        }

        return builder.ToString();
    }

    /// <summary>Formats a value in base 36, padded to at least <paramref name="padding"/> characters.</summary>
    private static string FormatBase36(long value, int padding)
    {
        var sign = value < 0 ? "-" : string.Empty;
        var remaining = Math.Abs(value);
        if (remaining == 0) return sign + "0".PadLeft(padding, '0');

        var builder = new StringBuilder();
        while (remaining > 0)
        {
            builder.Insert(0, Base36Digits[(int)(remaining % 36)]);
            remaining /= 36;
        }

        return sign + builder.ToString().PadLeft(padding, '0');
    }
}
