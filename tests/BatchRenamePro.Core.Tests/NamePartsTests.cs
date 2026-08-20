using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class NamePartsTests
{
    [TestMethod]
    [DataRow("report.txt", "report", ".txt")]
    [DataRow("archive.tar.gz", "archive.tar", ".gz")]
    [DataRow("README", "README", "")]
    public void Split_SeparatesAtTheLastDot(string input, string expectedBase, string expectedExtension)
    {
        var parts = NameParts.Split(input);

        Assert.AreEqual(expectedBase, parts.BaseName);
        Assert.AreEqual(expectedExtension, parts.Extension);
        Assert.AreEqual(input, parts.FullName);
    }

    [TestMethod]
    public void Split_TreatsALeadingDotAsPartOfTheName()
    {
        // ".gitignore" is a name, not an extension with nothing in front of it.
        var parts = NameParts.Split(".gitignore");

        Assert.AreEqual(".gitignore", parts.BaseName);
        Assert.AreEqual("", parts.Extension);
    }

    [TestMethod]
    public void Split_TreatsATrailingDotAsPartOfTheName()
    {
        var parts = NameParts.Split("report.");

        Assert.AreEqual("report.", parts.BaseName);
        Assert.AreEqual("", parts.Extension);
    }

    [TestMethod]
    public void Split_GivesDirectoriesNoExtension()
    {
        // A folder called "v1.2" has no extension; renaming its "extension" would be nonsense.
        var parts = NameParts.Split("v1.2", isDirectory: true);

        Assert.AreEqual("v1.2", parts.BaseName);
        Assert.AreEqual("", parts.Extension);
    }

    [TestMethod]
    public void Transform_OnExtensionScope_HidesTheDotFromTheTransform()
    {
        var parts = NameParts.Split("photo.JPG");

        var result = RenameScope.Extension.Transform(parts, Fixture.Context("photo.JPG"), value =>
        {
            Assert.AreEqual("JPG", value, "the transform should not have to deal with the dot");
            return value.ToLowerInvariant();
        });

        Assert.AreEqual("photo.jpg", result.FullName);
    }

    [TestMethod]
    public void Transform_OnFullNameScope_ResplitsTheResult()
    {
        var parts = NameParts.Split("a.txt");

        var result = RenameScope.FullName.Transform(parts, Fixture.Context("a.txt"), _ => "b.md");

        Assert.AreEqual("b", result.BaseName);
        Assert.AreEqual(".md", result.Extension);
    }
}
