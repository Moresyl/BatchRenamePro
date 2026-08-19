using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class TokenEngineTests
{
    private static string Expand(string pattern, string name = "report.txt", int index = 0, int total = 1) =>
        TokenEngine.Expand(pattern, NameParts.Split(name), Fixture.Context(name, index, total), SequenceOptions.Default);

    [TestMethod]
    public void Expand_SubstitutesTheNameAndExtension()
    {
        Assert.AreEqual("report", Expand("{name}"));
        Assert.AreEqual("txt", Expand("{ext}"));
        Assert.AreEqual("report.txt", Expand("{fullname}"));
        Assert.AreEqual("docs", Expand("{parent}"));
    }

    [TestMethod]
    public void Expand_ResolvesNamesAgainstThePipelineNotTheDisk()
    {
        // A pattern rule running after a case rule must see the case rule's output, or the pipeline
        // silently throws earlier work away.
        var context = Fixture.Context("report.txt");
        var afterAnEarlierRule = new NameParts("REPORT", ".txt");

        Assert.AreEqual("REPORT", TokenEngine.Expand("{name}", afterAnEarlierRule, context, SequenceOptions.Default));
        Assert.AreEqual("report", TokenEngine.Expand("{original}", afterAnEarlierRule, context, SequenceOptions.Default));
    }

    [TestMethod]
    public void Expand_DoesNotRescanSubstitutedText()
    {
        // The literal name contains a '#'. If expansion were a chain of String.Replace calls, that
        // '#' would be re-read as a sequence token and the name would come out mangled.
        Assert.AreEqual("report#2", Expand("{name}", "report#2.txt"));
    }

    [TestMethod]
    public void Expand_HonoursBraceEscapes()
    {
        Assert.AreEqual("{name}", Expand("{{name}}"));
        Assert.AreEqual("{literal", Expand("{{literal"));
    }

    [TestMethod]
    public void Expand_EmitsAnUnterminatedBraceLiterally()
    {
        Assert.AreEqual("a {name", Expand("a {name"));
    }

    [TestMethod]
    public void Expand_EmitsUnknownTokensVerbatimSoTyposAreVisible()
    {
        Assert.AreEqual("{nmae}", Expand("{nmae}"));
        CollectionAssert.AreEqual(new[] { "nmae" }, TokenEngine.FindUnknownTokens("{nmae}-{name}").ToArray());
    }

    [TestMethod]
    public void Expand_AppliesTextFormats()
    {
        Assert.AreEqual("REPORT", Expand("{name:upper}"));
        Assert.AreEqual("annual report", Expand("{name:lower}", "Annual Report.txt"));
        Assert.AreEqual("Annual Report", Expand("{name:title}", "ANNUAL REPORT.txt"));
    }

    [TestMethod]
    public void Expand_FormatsTimeTokensFromTheBatchTimestamp()
    {
        Assert.AreEqual("20260819", Expand("{date}"));
        Assert.AreEqual("2026-08", Expand("{date:yyyy-MM}"));
        Assert.AreEqual("143045", Expand("{time}"));
        Assert.AreEqual("2025-11-27", Expand("{modified:yyyy-MM-dd}"));
        Assert.AreEqual("2024-03-04", Expand("{created:yyyy-MM-dd}"));
    }

    [TestMethod]
    public void Expand_FormatsSizes()
    {
        Assert.AreEqual("2097152", Expand("{size}"));
        Assert.AreEqual("2048", Expand("{size:KB}"));
        Assert.AreEqual("2MB", Expand("{size:auto}"));
    }

    [TestMethod]
    public void Expand_NumbersItemsFromTheirBatchPosition()
    {
        Assert.AreEqual("01", Expand("{index}", index: 0));
        Assert.AreEqual("03", Expand("{index}", index: 2));
        Assert.AreEqual("0003", Expand("{index:0000}", index: 2));
        Assert.AreEqual("7", Expand("{total}", total: 7));
    }

    [TestMethod]
    public void Expand_SupportsLegacyStarAndHashSyntax()
    {
        Assert.AreEqual("report", Expand("*"));
        Assert.AreEqual("01", Expand("#", index: 0));
        Assert.AreEqual("0001", Expand("####", index: 0));
        Assert.AreEqual("report-0003", Expand("*-####", index: 2));
    }

    [TestMethod]
    public void Expand_ProducesStableGuidsAndRandomsForTheSameItem()
    {
        // The preview re-runs on every keystroke. A token that changed each time would make the
        // preview useless and the result unpredictable.
        var first = Expand("{guid}-{rand}");
        var second = Expand("{guid}-{rand}");

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, Expand("{guid}-{rand}", index: 1));
    }

    [TestMethod]
    public void Catalog_OnlyOffersTokensTheEngineUnderstands()
    {
        foreach (var descriptor in TokenEngine.Catalog)
            Assert.IsEmpty(TokenEngine.FindUnknownTokens(descriptor.Token), descriptor.Token);
    }
}
