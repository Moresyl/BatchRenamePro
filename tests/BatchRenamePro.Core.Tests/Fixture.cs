using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Tests;

/// <summary>Shared helpers that keep the tests focused on behaviour rather than plumbing.</summary>
internal static class Fixture
{
    /// <summary>A fixed batch timestamp, so time tokens are assertable.</summary>
    public static readonly DateTimeOffset Timestamp = new(2026, 8, 19, 14, 30, 45, TimeSpan.FromHours(8));

    public static readonly FileFacts Facts = new(
        new DateTimeOffset(2024, 3, 4, 9, 0, 0, TimeSpan.FromHours(8)),
        new DateTimeOffset(2025, 11, 27, 18, 45, 0, TimeSpan.FromHours(8)),
        2_097_152);

    public static RenameContext Context(
        string name = "report.txt",
        int index = 0,
        int total = 1,
        bool isDirectory = false,
        FileFacts? facts = null) =>
        new(Path.Combine(@"C:\work\docs", name), isDirectory, index, total, Timestamp, facts ?? Facts);

    /// <summary>Runs one rule over a name and returns the full result, extension included.</summary>
    public static string Apply(IRenameRule rule, string name, RenameContext? context = null)
    {
        var resolved = context ?? Context(name);
        return rule.Apply(NameParts.Split(name, resolved.IsDirectory), resolved).FullName;
    }
}
