using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class SequenceFormatterTests
{
    [TestMethod]
    public void Default_AppliesThePrimaryConstructorDefaults()
    {
        // Guards the record-struct trap: `new SequenceOptions()` zero-initialises and would give a
        // step of 0, which numbers every file the same.
        Assert.AreEqual(1, SequenceOptions.Default.Start);
        Assert.AreEqual(1, SequenceOptions.Default.Step);
        Assert.AreEqual(2, SequenceOptions.Default.Padding);
        Assert.AreEqual(SequenceStyle.Numeric, SequenceOptions.Default.Style);
        Assert.AreEqual(1, SequenceOptions.Default.GroupSize);
    }

    [TestMethod]
    public void Format_PadsToTheRequestedWidth()
    {
        var options = SequenceOptions.Default with { Padding = 3 };

        Assert.AreEqual("001", SequenceFormatter.Format(0, options));
        Assert.AreEqual("010", SequenceFormatter.Format(9, options));
    }

    [TestMethod]
    public void Format_TreatsPaddingAsAMinimumNotAWidth()
    {
        // Two-digit padding must not wrap or truncate once a batch passes 99.
        Assert.AreEqual("100", SequenceFormatter.Format(99, SequenceOptions.Default));
    }

    [TestMethod]
    public void Format_HonoursStartAndStep()
    {
        var options = SequenceOptions.Default with { Start = 10, Step = 5 };

        Assert.AreEqual("10", SequenceFormatter.Format(0, options));
        Assert.AreEqual("20", SequenceFormatter.Format(2, options));
    }

    [TestMethod]
    public void Format_AdvancesOncePerGroup()
    {
        var options = SequenceOptions.Default with { GroupSize = 2 };

        Assert.AreEqual("01", SequenceFormatter.Format(0, options));
        Assert.AreEqual("01", SequenceFormatter.Format(1, options));
        Assert.AreEqual("02", SequenceFormatter.Format(2, options));
    }

    [TestMethod]
    public void Format_CountsInAlphabeticStyleLikeSpreadsheetColumns()
    {
        var options = SequenceOptions.Default with { Style = SequenceStyle.Alphabetic };

        Assert.AreEqual("A", SequenceFormatter.Format(0, options));
        Assert.AreEqual("Z", SequenceFormatter.Format(25, options));
        Assert.AreEqual("AA", SequenceFormatter.Format(26, options));
    }

    [TestMethod]
    public void Format_CountsInRomanStyle()
    {
        var options = SequenceOptions.Default with { Style = SequenceStyle.Roman };

        Assert.AreEqual("I", SequenceFormatter.Format(0, options));
        Assert.AreEqual("IV", SequenceFormatter.Format(3, options));
        Assert.AreEqual("MCMXCIX", SequenceFormatter.Format(1998, options));
    }

    [TestMethod]
    public void Format_CountsInBase36()
    {
        var options = SequenceOptions.Default with { Style = SequenceStyle.Base36, Padding = 1 };

        Assert.AreEqual("1", SequenceFormatter.Format(0, options));
        Assert.AreEqual("a", SequenceFormatter.Format(9, options));
        Assert.AreEqual("10", SequenceFormatter.Format(35, options));
    }

    [TestMethod]
    public void Format_FallsBackToDecimalWhenAStyleCannotRepresentTheValue()
    {
        var roman = SequenceOptions.Default with { Style = SequenceStyle.Roman, Start = 0 };
        var alphabetic = SequenceOptions.Default with { Style = SequenceStyle.Alphabetic, Start = 0 };

        Assert.AreEqual("0", SequenceFormatter.Format(0, roman));
        Assert.AreEqual("0", SequenceFormatter.Format(0, alphabetic));
    }

    [TestMethod]
    public void Settings_RoundTripThroughOptions()
    {
        var settings = new SequenceSettings { Start = 5, Step = 2, Padding = 4, Style = SequenceStyle.Roman, GroupSize = 3 };

        var options = settings.ToOptions();

        Assert.AreEqual(5, options.Start);
        Assert.AreEqual(2, options.Step);
        Assert.AreEqual(4, options.Padding);
        Assert.AreEqual(SequenceStyle.Roman, options.Style);
        Assert.AreEqual(3, options.GroupSize);
    }
}
