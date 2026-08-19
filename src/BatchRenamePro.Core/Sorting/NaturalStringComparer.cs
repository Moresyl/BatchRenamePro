namespace BatchRenamePro.Core.Sorting;

/// <summary>
/// Orders names the way Explorer does, so <c>file2</c> sorts before <c>file10</c>.
/// </summary>
/// <remarks>
/// Ordinary string comparison puts <c>file10</c> first because <c>1</c> precedes <c>2</c>, which is
/// exactly wrong when the whole point of the batch is to number things in order. Runs of digits are
/// compared as numbers, everything else case-insensitively.
/// </remarks>
public sealed class NaturalStringComparer : IComparer<string>
{
    /// <summary>A shared, thread-safe instance.</summary>
    public static NaturalStringComparer OrdinalIgnoreCase { get; } = new();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var leftIndex = 0;
        var rightIndex = 0;

        while (leftIndex < x.Length && rightIndex < y.Length)
        {
            if (char.IsDigit(x[leftIndex]) && char.IsDigit(y[rightIndex]))
            {
                var leftStart = leftIndex;
                var rightStart = rightIndex;
                while (leftIndex < x.Length && char.IsDigit(x[leftIndex])) leftIndex++;
                while (rightIndex < y.Length && char.IsDigit(y[rightIndex])) rightIndex++;

                // Leading zeros carry no value, so "007" and "7" compare equal in magnitude.
                var leftDigits = x.AsSpan(leftStart, leftIndex - leftStart).TrimStart('0');
                var rightDigits = y.AsSpan(rightStart, rightIndex - rightStart).TrimStart('0');

                var lengthComparison = leftDigits.Length.CompareTo(rightDigits.Length);
                if (lengthComparison != 0) return lengthComparison;

                var numberComparison = leftDigits.CompareTo(rightDigits, StringComparison.Ordinal);
                if (numberComparison != 0) return numberComparison;
            }
            else
            {
                var left = char.ToUpperInvariant(x[leftIndex]);
                var right = char.ToUpperInvariant(y[rightIndex]);

                var comparison = left.CompareTo(right);
                if (comparison != 0) return comparison;

                leftIndex++;
                rightIndex++;
            }
        }

        return x.Length.CompareTo(y.Length);
    }
}
