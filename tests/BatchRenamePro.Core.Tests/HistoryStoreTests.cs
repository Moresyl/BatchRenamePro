using BatchRenamePro.Core.Execution;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class HistoryStoreTests
{
    private static RenameTransaction Transaction(string id, int operations = 2) => new(
        id,
        Fixture.Timestamp,
        Fixture.Timestamp.AddSeconds(3),
        [.. Enumerable.Range(0, operations).Select(i => new RenameOperation($@"C:\work\a{i}.txt", $@"C:\work\b{i}.txt", false))],
        $"batch {id}");

    [TestMethod]
    public async Task SaveAndLoad_RoundTripsATransaction()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonHistoryStore(directory.Path);

        await store.SaveAsync(Transaction("20260819-000001"));
        var loaded = (await store.LoadRecentAsync()).Single();

        Assert.AreEqual("20260819-000001", loaded.Id);
        Assert.AreEqual(2, loaded.Count);
        Assert.AreEqual(@"C:\work\a0.txt", loaded.Operations[0].SourcePath);
        Assert.AreEqual(TimeSpan.FromSeconds(3), loaded.Duration);
    }

    [TestMethod]
    public async Task LoadRecent_ReturnsTheNewestFirstAndHonoursTheLimit()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonHistoryStore(directory.Path);

        for (var i = 1; i <= 5; i++) await store.SaveAsync(Transaction($"2026081{i}-00000{i}"));

        var loaded = await store.LoadRecentAsync(3);

        // The whole order, not just the head. Five saves in a loop land on one last-write time —
        // the clock behind it moves in 15ms steps — so this is what pins the tie-break down. Check
        // only the newest and the sort could fall back on directory order without anyone noticing.
        CollectionAssert.AreEqual(
            new[] { "20260815-000005", "20260814-000004", "20260813-000003" },
            loaded.Select(transaction => transaction.Id).ToArray());
    }

    [TestMethod]
    public async Task Delete_RemovesOneEntry()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonHistoryStore(directory.Path);
        await store.SaveAsync(Transaction("a1"));
        await store.SaveAsync(Transaction("b2"));

        await store.DeleteAsync("a1");

        Assert.HasCount(1, await store.LoadRecentAsync());
    }

    [TestMethod]
    public async Task Prune_KeepsOnlyTheMostRecentEntries()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonHistoryStore(directory.Path);
        for (var i = 1; i <= 6; i++) await store.SaveAsync(Transaction($"2026081{i}-00000{i}"));

        await store.PruneAsync(2);

        // Pruning has to agree with loading about which entries are the newest, or it deletes the
        // wrong four. Same tied last-write time as above, so the same tie-break is under test.
        var remaining = await store.LoadRecentAsync();
        CollectionAssert.AreEqual(
            new[] { "20260816-000006", "20260815-000005" },
            remaining.Select(transaction => transaction.Id).ToArray());
    }

    [TestMethod]
    public async Task Load_SkipsAnUnreadableEntry()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonHistoryStore(directory.Path);
        await store.SaveAsync(Transaction("good"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "broken.json"), "{ truncated");

        Assert.HasCount(1, await store.LoadRecentAsync());
    }

    [TestMethod]
    public async Task LoadRecent_ReturnsNothingWhenTheFolderHasNeverBeenUsed()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonHistoryStore(Path.Combine(directory.Path, "not-created-yet"));

        Assert.IsEmpty(await store.LoadRecentAsync());
    }
}
