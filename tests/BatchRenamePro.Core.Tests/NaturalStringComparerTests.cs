using BatchRenamePro.Core.Sorting;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class NaturalStringComparerTests
{
    [TestMethod]
    public void Sort_OrdersNumbersByValueNotByDigit()
    {
        string[] names = ["file10.txt", "file2.txt", "file1.txt"];

        Array.Sort(names, NaturalStringComparer.OrdinalIgnoreCase);

        CollectionAssert.AreEqual(new[] { "file1.txt", "file2.txt", "file10.txt" }, names);
    }

    [TestMethod]
    public void Compare_IgnoresLeadingZeros()
    {
        Assert.AreEqual(0, NaturalStringComparer.OrdinalIgnoreCase.Compare("img007.jpg", "img007.jpg"));
        Assert.IsLessThan(0, NaturalStringComparer.OrdinalIgnoreCase.Compare("img007.jpg", "img8.jpg"));
    }

    [TestMethod]
    public void Compare_IgnoresCase()
    {
        Assert.AreEqual(0, NaturalStringComparer.OrdinalIgnoreCase.Compare("Report.TXT", "report.txt"));
    }

    [TestMethod]
    public void Compare_PutsAShorterPrefixFirst()
    {
        Assert.IsLessThan(0, NaturalStringComparer.OrdinalIgnoreCase.Compare("report", "report-final"));
    }

    [TestMethod]
    public void Compare_HandlesNulls()
    {
        Assert.AreEqual(0, NaturalStringComparer.OrdinalIgnoreCase.Compare(null, null));
        Assert.IsLessThan(0, NaturalStringComparer.OrdinalIgnoreCase.Compare(null, "a"));
        Assert.IsGreaterThan(0, NaturalStringComparer.OrdinalIgnoreCase.Compare("a", null));
    }
}
