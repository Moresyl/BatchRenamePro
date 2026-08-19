using BatchRenamePro.Core.Scanning;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class FileScannerTests
{
    private static TemporaryDirectory Tree()
    {
        var directory = new TemporaryDirectory();
        directory.CreateFile("photo1.jpg");
        directory.CreateFile("photo10.jpg");
        directory.CreateFile("photo2.jpg");
        directory.CreateFile("notes.txt");

        var nested = Path.Combine(directory.Path, "sub");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "deep.jpg"), "");

        return directory;
    }

    private static string[] NamesOf(IEnumerable<BatchRenamePro.Core.Planning.RenameSource> sources) =>
        [.. sources.Select(source => Path.GetFileName(source.Path))];

    [TestMethod]
    public void Default_TakesTheFoldersOwnFilesInNaturalOrder()
    {
        using var directory = Tree();

        var found = new FileScanner().Scan(directory.Path);

        CollectionAssert.AreEqual(new[] { "notes.txt", "photo1.jpg", "photo2.jpg", "photo10.jpg" }, NamesOf(found));
    }

    [TestMethod]
    public void ScanOptionsDefault_AppliesThePrimaryConstructorDefaults()
    {
        // Guards the record-struct trap: `new ScanOptions()` would leave MaxDepth at 0.
        Assert.AreEqual(16, ScanOptions.Default.MaxDepth);
        Assert.AreEqual(ScanTarget.Files, ScanOptions.Default.Target);
    }

    [TestMethod]
    public void Recursive_DescendsIntoSubfolders()
    {
        using var directory = Tree();

        var found = new FileScanner().Scan(directory.Path, ScanOptions.Default with { Recursive = true });

        CollectionAssert.Contains(NamesOf(found), "deep.jpg");
    }

    [TestMethod]
    public void MaxDepth_StopsTheDescent()
    {
        using var directory = Tree();

        var found = new FileScanner().Scan(directory.Path, ScanOptions.Default with { Recursive = true, MaxDepth = 0 });

        CollectionAssert.DoesNotContain(NamesOf(found), "deep.jpg");
    }

    [TestMethod]
    public void IncludePatterns_KeepOnlyMatchingNames()
    {
        using var directory = Tree();

        var found = new FileScanner().Scan(directory.Path, ScanOptions.Default with { IncludePatterns = "*.jpg" });

        Assert.IsTrue(NamesOf(found).All(name => name.EndsWith(".jpg", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ExcludePatterns_RejectMatchingNamesAfterTheIncludeFilter()
    {
        using var directory = Tree();

        var found = new FileScanner().Scan(directory.Path, ScanOptions.Default with
        {
            IncludePatterns = "*.jpg;*.txt",
            ExcludePatterns = "photo1?.jpg"
        });

        CollectionAssert.DoesNotContain(NamesOf(found), "photo10.jpg");
        CollectionAssert.Contains(NamesOf(found), "photo1.jpg");
    }

    [TestMethod]
    public void Target_CanCollectFoldersInstead()
    {
        using var directory = Tree();

        var found = new FileScanner().Scan(directory.Path, ScanOptions.Default with { Target = ScanTarget.Directories });

        CollectionAssert.AreEqual(new[] { "sub" }, NamesOf(found));
        Assert.IsTrue(found[0].IsDirectory);
    }

    [TestMethod]
    public void RecursiveFolderScan_ListsChildrenBeforeTheirParent()
    {
        // Renaming a folder invalidates the paths inside it, so children have to come first.
        using var directory = new TemporaryDirectory();
        var outer = Path.Combine(directory.Path, "outer");
        var inner = Path.Combine(outer, "inner");
        Directory.CreateDirectory(inner);

        var found = new FileScanner().Scan(directory.Path, ScanOptions.Default with
        {
            Target = ScanTarget.Directories,
            Recursive = true
        });

        CollectionAssert.AreEqual(new[] { "inner", "outer" }, NamesOf(found));
    }

    [TestMethod]
    public void MissingFolder_ScansToNothingRatherThanThrowing()
    {
        Assert.IsEmpty(new FileScanner().Scan(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }
}
