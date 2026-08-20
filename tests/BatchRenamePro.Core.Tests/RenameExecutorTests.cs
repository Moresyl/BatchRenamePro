using System.Collections.Concurrent;
using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Execution;
using BatchRenamePro.Core.Planning;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class RenameExecutorTests
{
    private static readonly PlanOptions Deterministic = new(ConflictPolicy.Block, Fixture.Timestamp);

    private static RenamePlan PlanFor(TemporaryDirectory directory, params (string From, string To)[] moves)
    {
        var sources = moves
            .Select(move => new RenameSource(Path.Combine(directory.Path, move.From), false, Fixture.Facts))
            .ToArray();

        var map = moves.ToDictionary(
            move => Path.GetFileNameWithoutExtension(move.From),
            move => Path.GetFileNameWithoutExtension(move.To),
            StringComparer.Ordinal);

        return new RenamePlanner().Build(sources, [new MappingRule(map)], Deterministic);
    }

    private static string[] NamesIn(TemporaryDirectory directory) =>
        [.. Directory.GetFiles(directory.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    [TestMethod]
    public async Task ExecuteAsync_RenamesEveryReadyItem()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt", "one");
        directory.CreateFile("b.txt", "two");

        var transaction = await new RenameExecutor().ExecuteAsync(PlanFor(directory, ("a.txt", "x.txt"), ("b.txt", "y.txt")));

        CollectionAssert.AreEqual(new[] { "x.txt", "y.txt" }, NamesIn(directory));
        Assert.AreEqual(2, transaction.Count);
        Assert.AreEqual("one", File.ReadAllText(Path.Combine(directory.Path, "x.txt")));
    }

    [TestMethod]
    public async Task ExecuteAsync_SwapsTwoNames()
    {
        // The classic case a plain sequence of moves cannot do: neither item can move first.
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt", "content-a");
        directory.CreateFile("b.txt", "content-b");

        await new RenameExecutor().ExecuteAsync(PlanFor(directory, ("a.txt", "b.txt"), ("b.txt", "a.txt")));

        Assert.AreEqual("content-b", File.ReadAllText(Path.Combine(directory.Path, "a.txt")));
        Assert.AreEqual("content-a", File.ReadAllText(Path.Combine(directory.Path, "b.txt")));
    }

    [TestMethod]
    public async Task ExecuteAsync_ShiftsAChainInTheRightOrder()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt", "content-a");
        directory.CreateFile("b.txt", "content-b");

        await new RenameExecutor().ExecuteAsync(PlanFor(directory, ("a.txt", "b.txt"), ("b.txt", "c.txt")));

        CollectionAssert.AreEqual(new[] { "b.txt", "c.txt" }, NamesIn(directory));
        Assert.AreEqual("content-a", File.ReadAllText(Path.Combine(directory.Path, "b.txt")));
        Assert.AreEqual("content-b", File.ReadAllText(Path.Combine(directory.Path, "c.txt")));
    }

    [TestMethod]
    public async Task ExecuteAsync_HandlesACaseOnlyRename()
    {
        // Windows treats "a.txt" and "A.txt" as the same path, so this only works via staging.
        using var directory = new TemporaryDirectory();
        directory.CreateFile("report.txt", "content");

        await new RenameExecutor().ExecuteAsync(PlanFor(directory, ("report.txt", "REPORT.txt")));

        CollectionAssert.AreEqual(new[] { "REPORT.txt" }, NamesIn(directory));
    }

    [TestMethod]
    public async Task ExecuteAsync_LeavesNoStagingFilesBehind()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");

        await new RenameExecutor().ExecuteAsync(PlanFor(directory, ("a.txt", "b.txt"), ("b.txt", "a.txt")));

        Assert.IsFalse(NamesIn(directory).Any(name => name.Contains("batchrename", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ExecuteAsync_ReportsProgressForEveryItem()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");

        // Progress<T> hands each callback to the thread pool, so two reports can land at once —
        // a plain List would lose one of them and the test would fail for the wrong reason.
        var reports = new ConcurrentQueue<RenameProgress>();
        await new RenameExecutor().ExecuteAsync(
            PlanFor(directory, ("a.txt", "x.txt"), ("b.txt", "y.txt")),
            new Progress<RenameProgress>(reports.Enqueue));

        // Those callbacks are asynchronous, so allow them to drain.
        for (var attempt = 0; attempt < 100 && reports.Count < 2; attempt++) await Task.Delay(10);

        // For the same reason the two reports can also arrive out of order, so what is checked is
        // the set of them: one report per item, counting up, ending at a full bar.
        Assert.HasCount(2, reports);
        CollectionAssert.AreEqual(new[] { 1, 2 }, reports.Select(report => report.Completed).Order().ToArray());
        Assert.IsTrue(reports.All(report => report.Total == 2));
        Assert.AreEqual(1.0, reports.Max(report => report.Fraction), 0.001);
    }

    [TestMethod]
    public async Task ExecuteAsync_RefusesAPlanThatStillHasProblems()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");
        directory.CreateFile("c.txt");

        var plan = PlanFor(directory, ("a.txt", "b.txt"), ("b.txt", "c.txt"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new RenameExecutor().ExecuteAsync(plan));
    }

    [TestMethod]
    public async Task ExecuteAsync_RefusesABatchWhoseItemVanishedAfterThePreview()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt", "content-a");
        directory.CreateFile("b.txt", "content-b");

        var plan = PlanFor(directory, ("a.txt", "x.txt"), ("b.txt", "y.txt"));

        // Deleted between preview and execute — the usual way a plan goes stale.
        File.Delete(Path.Combine(directory.Path, "b.txt"));

        var error = await Assert.ThrowsExactlyAsync<RenameFailedException>(() => new RenameExecutor().ExecuteAsync(plan));

        // The pre-flight check runs before anything moves, so nothing is half-renamed and the user
        // has nothing to clean up by hand.
        Assert.IsTrue(error.RolledBack);
        Assert.IsEmpty(error.Unrecovered);
        CollectionAssert.AreEqual(new[] { "a.txt" }, NamesIn(directory));
    }

    [TestMethod]
    public async Task ExecuteAsync_RestoresEveryOriginalNameWhenAMoveFailsMidBatch()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt", "content-a");
        directory.CreateFile("b.txt", "content-b");

        var plan = PlanFor(directory, ("a.txt", "x.txt"), ("b.txt", "y.txt"));

        // A file open with no sharing cannot be renamed, so the batch fails on its second item —
        // after the first one has already moved. This is the case the unwind exists for.
        using (new FileStream(Path.Combine(directory.Path, "b.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var error = await Assert.ThrowsExactlyAsync<RenameFailedException>(() => new RenameExecutor().ExecuteAsync(plan));

            Assert.IsTrue(error.RolledBack);
            Assert.IsEmpty(error.Unrecovered);
        }

        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, NamesIn(directory));
        Assert.AreEqual("content-a", File.ReadAllText(Path.Combine(directory.Path, "a.txt")));
    }

    [TestMethod]
    public async Task RevertAsync_PutsEverythingBack()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt", "content-a");
        directory.CreateFile("b.txt", "content-b");

        var executor = new RenameExecutor();
        var transaction = await executor.ExecuteAsync(PlanFor(directory, ("a.txt", "x.txt"), ("b.txt", "y.txt")));

        await executor.RevertAsync(transaction);

        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, NamesIn(directory));
        Assert.AreEqual("content-a", File.ReadAllText(Path.Combine(directory.Path, "a.txt")));
    }

    [TestMethod]
    public async Task RevertAsync_UndoesASwap()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt", "content-a");
        directory.CreateFile("b.txt", "content-b");

        var executor = new RenameExecutor();
        var transaction = await executor.ExecuteAsync(PlanFor(directory, ("a.txt", "b.txt"), ("b.txt", "a.txt")));

        await executor.RevertAsync(transaction);

        Assert.AreEqual("content-a", File.ReadAllText(Path.Combine(directory.Path, "a.txt")));
        Assert.AreEqual("content-b", File.ReadAllText(Path.Combine(directory.Path, "b.txt")));
    }

    [TestMethod]
    public async Task ExecuteAsync_RenamesFolders()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "old");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "inner.txt"), "kept");

        var plan = new RenamePlanner().Build(
            [new RenameSource(source, true, Fixture.Facts)],
            [new MappingRule(new Dictionary<string, string>(StringComparer.Ordinal) { ["old"] = "new" })],
            Deterministic);

        await new RenameExecutor().ExecuteAsync(plan);

        Assert.IsTrue(Directory.Exists(Path.Combine(directory.Path, "new")));
        Assert.AreEqual("kept", File.ReadAllText(Path.Combine(directory.Path, "new", "inner.txt")));
    }

    /// <summary>Builds the folder-and-contents batch the nested tests below share.</summary>
    /// <remarks>
    /// <c>old\inner.txt</c> and <c>old</c> itself, in the order a recursive scan yields them:
    /// contents first, the folder that holds them last.
    /// </remarks>
    private static RenamePlan NestedPlan(TemporaryDirectory directory)
    {
        var folder = Path.Combine(directory.Path, "old");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "inner.txt"), "kept");

        return new RenamePlanner().Build(
            [
                new RenameSource(Path.Combine(folder, "inner.txt"), false, Fixture.Facts),
                new RenameSource(folder, true, Fixture.Facts)
            ],
            [
                new MappingRule(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["inner"] = "renamed",
                    ["old"] = "new"
                })
            ],
            Deterministic);
    }

    [TestMethod]
    public async Task ExecuteAsync_RenamesAFolderAndItsContentsInOneBatch()
    {
        using var directory = new TemporaryDirectory();

        await new RenameExecutor().ExecuteAsync(NestedPlan(directory));

        Assert.IsFalse(Directory.Exists(Path.Combine(directory.Path, "old")));
        Assert.AreEqual("kept", File.ReadAllText(Path.Combine(directory.Path, "new", "renamed.txt")));
    }

    [TestMethod]
    public async Task RevertAsync_UndoesAFolderRenamedAlongsideItsContents()
    {
        // The undo replays the batch backwards, so the folder goes back to its old name before the
        // file inside it does. Every path the transaction recorded for that file was written against
        // the original tree and only becomes real again once the folder has moved back, which is
        // what the pre-flight check has to allow for.
        using var directory = new TemporaryDirectory();

        var executor = new RenameExecutor();
        var transaction = await executor.ExecuteAsync(NestedPlan(directory));

        await executor.RevertAsync(transaction);

        Assert.IsFalse(Directory.Exists(Path.Combine(directory.Path, "new")));
        Assert.AreEqual("kept", File.ReadAllText(Path.Combine(directory.Path, "old", "inner.txt")));
    }

    [TestMethod]
    public async Task ExecuteAsync_RenamesContentsEvenWhenTheFolderIsListedFirst()
    {
        // A recursive scan puts the folder last, but a user who adds items by hand can arrange them
        // any way at all. Once the folder has moved, the path recorded for the file inside it points
        // at nothing, and the executor has to follow the folder to find it.
        using var directory = new TemporaryDirectory();
        var folder = Path.Combine(directory.Path, "old");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "inner.txt"), "kept");

        var plan = new RenamePlanner().Build(
            [
                new RenameSource(folder, true, Fixture.Facts),
                new RenameSource(Path.Combine(folder, "inner.txt"), false, Fixture.Facts)
            ],
            [
                new MappingRule(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["old"] = "new",
                    ["inner"] = "renamed"
                })
            ],
            Deterministic);

        await new RenameExecutor().ExecuteAsync(plan);

        Assert.AreEqual("kept", File.ReadAllText(Path.Combine(directory.Path, "new", "renamed.txt")));
    }
}
