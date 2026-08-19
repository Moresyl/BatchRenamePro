using BatchRenamePro.Core.Planning;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class FileNameValidatorTests
{
    [TestMethod]
    [DataRow("report.txt")]
    [DataRow(".gitignore")]
    [DataRow("报告 2026.docx")]
    [DataRow("CONTRACT.pdf")]
    public void Validate_AcceptsUsableNames(string name) => Assert.IsNull(FileNameValidator.Validate(name));

    [TestMethod]
    [DataRow("", "name.empty")]
    [DataRow("   ", "name.empty")]
    [DataRow("a:b.txt", "name.invalidChars")]
    [DataRow("a|b.txt", "name.invalidChars")]
    [DataRow("report.", "name.trailingDotOrSpace")]
    [DataRow("report ", "name.trailingDotOrSpace")]
    [DataRow(" report.txt", "name.leadingSpace")]
    public void Validate_RejectsNamesWindowsWouldMangle(string name, string expectedCode) =>
        Assert.AreEqual(expectedCode, FileNameValidator.Validate(name)?.Code);

    [TestMethod]
    [DataRow("NUL")]
    [DataRow("nul.txt")]
    [DataRow("COM1.log")]
    [DataRow("LPT9")]
    public void Validate_RejectsReservedDeviceNamesWithOrWithoutAnExtension(string name) =>
        Assert.AreEqual("name.reserved", FileNameValidator.Validate(name)?.Code);

    [TestMethod]
    public void Validate_RejectsNamesLongerThanAPathComponentAllows() =>
        Assert.AreEqual("name.tooLong", FileNameValidator.Validate(new string('a', FileNameValidator.MaxComponentLength + 1))?.Code);
}
