using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Rules;
using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class RuleTests
{
    [TestMethod]
    public void PatternRule_KeepsTheExtensionWhenScopedToTheBaseName()
    {
        var rule = new PatternRule { Pattern = "{name}_{index}", Scope = RenameScope.BaseName };

        Assert.AreEqual("report_01.txt", Fixture.Apply(rule, "report.txt"));
    }

    [TestMethod]
    public void PatternRule_ReportsAnEmptyPatternAsAnError()
    {
        var rule = new PatternRule { Pattern = "" };

        Assert.IsTrue(rule.Validate().Any(d => d.Code == "rule.pattern.empty" && d.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public void PatternRule_ReportsAnUnknownTokenAsAWarningNotAnError()
    {
        // A typo should show up in the preview, not stop the user from working.
        var rule = new PatternRule { Pattern = "{nmae}" };

        var diagnostic = rule.Validate().Single(d => d.Code == "rule.pattern.unknownToken");
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [TestMethod]
    public void ReplaceRule_ReplacesLiteralTextIgnoringCaseByDefault()
    {
        var rule = new ReplaceRule { Find = "DRAFT", ReplaceWith = "final" };

        Assert.AreEqual("final-final.txt", Fixture.Apply(rule, "draft-Draft.txt"));
    }

    [TestMethod]
    public void ReplaceRule_CanStopAfterTheFirstMatch()
    {
        var rule = new ReplaceRule { Find = "a", ReplaceWith = "x", FirstOccurrenceOnly = true, IgnoreCase = false };

        Assert.AreEqual("xbab.txt", Fixture.Apply(rule, "abab.txt"));
    }

    [TestMethod]
    public void ReplaceRule_SupportsRegexWithCaptureGroups()
    {
        var rule = new ReplaceRule
        {
            Find = @"^(\d{4})-(\d{2})-(\d{2})",
            ReplaceWith = "$3.$2.$1",
            UseRegex = true
        };

        Assert.AreEqual("27.11.2025 notes.txt", Fixture.Apply(rule, "2025-11-27 notes.txt"));
    }

    [TestMethod]
    public void ReplaceRule_LeavesTheNameAloneWhenTheExpressionIsBroken()
    {
        // A half-typed expression is the normal state of the box while someone is typing it.
        var rule = new ReplaceRule { Find = "([unclosed", ReplaceWith = "x", UseRegex = true };

        Assert.AreEqual("report.txt", Fixture.Apply(rule, "report.txt"));
        Assert.IsTrue(rule.Validate().Any(d => d.Code == "rule.replace.badRegex"));
    }

    [TestMethod]
    public void ReplaceRule_RecompilesWhenTheExpressionChanges()
    {
        var rule = new ReplaceRule { Find = "a", ReplaceWith = "x", UseRegex = true };
        Assert.AreEqual("xbc.txt", Fixture.Apply(rule, "abc.txt"));

        rule.Find = "b";
        Assert.AreEqual("axc.txt", Fixture.Apply(rule, "abc.txt"));
    }

    [TestMethod]
    public void InsertRule_AddsAPrefixAndASuffix()
    {
        var prefix = new InsertRule { ReplacesName = false, Text = "2026_", Position = InsertPosition.Prefix };
        var suffix = new InsertRule { ReplacesName = false, Text = "_final", Position = InsertPosition.Suffix };

        Assert.AreEqual("2026_report.txt", Fixture.Apply(prefix, "report.txt"));
        Assert.AreEqual("report_final.txt", Fixture.Apply(suffix, "report.txt"));
    }

    [TestMethod]
    public void InsertRule_ReplacesTheNameByDefaultAndKeepsTheExtension()
    {
        // The default is the answer to "rename these to X", not "put X next to what they are called".
        var rule = new InsertRule { Text = "holiday" };

        Assert.AreEqual("holiday.txt", Fixture.Apply(rule, "report.txt"));
    }

    [TestMethod]
    public void InsertRule_ReachesTheOldNameThroughATokenWhileReplacing()
    {
        // Replacing is not a wall: {name} is how the text takes the old name back in, which is what
        // makes "replace by default" safe to be the default at all.
        var rule = new InsertRule { Text = "{name}_v2" };

        Assert.AreEqual("report_v2.txt", Fixture.Apply(rule, "report.txt"));
    }

    [TestMethod]
    public void InsertRule_LeavesTheNameAloneWhenReplacementTextExpandsToNothing()
    {
        // A blank name is not a rename, it is a lost file. An item with no extension expands
        // {ext} to nothing, and the rule stands down rather than handing on an empty name.
        var rule = new InsertRule { Text = "{ext}" };

        Assert.AreEqual("report", Fixture.Apply(rule, "report"));
    }

    [TestMethod]
    public void InsertRule_ExpandsTokensInItsText()
    {
        var rule = new InsertRule { ReplacesName = false, Text = "{modified:yyyy-MM-dd} ", Position = InsertPosition.Prefix };

        Assert.AreEqual("2025-11-27 report.txt", Fixture.Apply(rule, "report.txt"));
    }

    [TestMethod]
    public void InsertRule_ClampsAnOffsetPastTheEndInsteadOfThrowing()
    {
        var rule = new InsertRule { ReplacesName = false, Text = "X", Position = InsertPosition.AtIndex, Index = 99 };

        Assert.AreEqual("abX.txt", Fixture.Apply(rule, "ab.txt"));
    }

    [TestMethod]
    public void RemoveRule_RemovesByOffset()
    {
        Assert.AreEqual("port.txt", Fixture.Apply(new RemoveRule { Mode = RemoveMode.FromStart, Count = 2 }, "report.txt"));
        Assert.AreEqual("repo.txt", Fixture.Apply(new RemoveRule { Mode = RemoveMode.FromEnd, Count = 2 }, "report.txt"));
        // Position is a zero-based offset, so this takes out "po".
        Assert.AreEqual("rert.txt", Fixture.Apply(new RemoveRule { Mode = RemoveMode.Range, Position = 2, Count = 2 }, "report.txt"));
        Assert.AreEqual("report.txt", Fixture.Apply(new RemoveRule { Mode = RemoveMode.Range, Position = 99, Count = 2 }, "report.txt"));
    }

    [TestMethod]
    public void RemoveRule_RemovesByCharacterClass()
    {
        Assert.AreEqual("report.txt", Fixture.Apply(new RemoveRule { Mode = RemoveMode.Digits }, "report2025.txt"));
        Assert.AreEqual("a-b_c.txt", Fixture.Apply(new RemoveRule { Mode = RemoveMode.Symbols, KeepCharacters = "-_" }, "a-b_c!@#.txt"));
    }

    [TestMethod]
    public void RemoveRule_RemovesAroundAMarker()
    {
        Assert.AreEqual("report.txt", Fixture.Apply(new RemoveRule { Mode = RemoveMode.AfterMarker, Text = " - copy" }, "report - copy.txt"));
        Assert.AreEqual("report.txt", Fixture.Apply(new RemoveRule { Mode = RemoveMode.BeforeMarker, Text = "] " }, "[draft] report.txt"));
    }

    [TestMethod]
    public void CaseRule_ChangesCaseWithoutDependingOnTheSystemCulture()
    {
        // Invariant on purpose: a Turkish locale would otherwise turn "I" into a dotless i and change
        // the bytes actually written to disk.
        Assert.AreEqual("annual report.txt", Fixture.Apply(new CaseRule { Mode = CaseMode.Lower }, "Annual REPORT.txt"));
        Assert.AreEqual("ANNUAL REPORT.txt", Fixture.Apply(new CaseRule { Mode = CaseMode.Upper }, "Annual report.txt"));
        Assert.AreEqual("Annual Report.txt", Fixture.Apply(new CaseRule { Mode = CaseMode.Title }, "annual report.txt"));
        Assert.AreEqual("Annual report.txt", Fixture.Apply(new CaseRule { Mode = CaseMode.Sentence }, "annual report.txt"));
        Assert.AreEqual("aNNUAL rEPORT.txt", Fixture.Apply(new CaseRule { Mode = CaseMode.Invert }, "Annual Report.txt"));
    }

    [TestMethod]
    public void ExtensionRule_ChangesOnlyTheExtension()
    {
        Assert.AreEqual("Photo.jpg", Fixture.Apply(new ExtensionRule { Mode = ExtensionMode.Lower }, "Photo.JPG"));
        Assert.AreEqual("notes.md", Fixture.Apply(new ExtensionRule { Mode = ExtensionMode.Replace, NewExtension = "md" }, "notes.txt"));
        Assert.AreEqual("notes", Fixture.Apply(new ExtensionRule { Mode = ExtensionMode.Remove }, "notes.txt"));
        Assert.AreEqual("notes.txt", Fixture.Apply(new ExtensionRule { Mode = ExtensionMode.AddIfMissing, NewExtension = "txt" }, "notes"));
    }

    [TestMethod]
    public void ExtensionRule_LeavesDirectoriesAlone()
    {
        var context = Fixture.Context("v1.2", isDirectory: true);
        var rule = new ExtensionRule { Mode = ExtensionMode.Replace, NewExtension = "bak" };

        Assert.AreEqual("v1.2", Fixture.Apply(rule, "v1.2", context));
    }

    [TestMethod]
    public void FullNameScope_DoesNotInventAnExtensionForAFolder()
    {
        // A folder called "backup.2026" is one name that happens to contain a dot. A rule working on
        // the whole name re-splits its own result before handing it on, and if that split forgets it
        // is looking at a folder, every rule after it in the pipeline leaves ".2026" alone as though
        // it were an extension.
        var context = Fixture.Context("backup.2026", isDirectory: true);
        var parts = NameParts.Split("backup.2026", isDirectory: true);

        var upper = new CaseRule { Mode = CaseMode.Upper, Scope = RenameScope.FullName }.Apply(parts, context);

        Assert.AreEqual("BACKUP.2026", upper.BaseName);
        Assert.AreEqual(string.Empty, upper.Extension);

        var numbered = new NumberRule
        {
            ReplacesName = false,
            Position = InsertPosition.Suffix,
            Separator = "_",
            Sequence = new SequenceSettings { Start = 7, Padding = 1 }
        }.Apply(upper, context);

        Assert.AreEqual("BACKUP.2026_7", numbered.FullName);
    }

    [TestMethod]
    public void NumberRule_AppendsASeparatorAndACounter()
    {
        var rule = new NumberRule
        {
            ReplacesName = false,
            Position = InsertPosition.Suffix,
            Separator = "_",
            Sequence = new SequenceSettings { Start = 1, Padding = 3 }
        };

        Assert.AreEqual("report_003.txt", Fixture.Apply(rule, "report.txt", Fixture.Context("report.txt", index: 2)));
    }

    [TestMethod]
    public void NumberRule_ReplacesTheNameByDefaultAndKeepsTheExtension()
    {
        // "Number these" means 01.png, 02.png. Anything left of the old name is there because
        // the switch below was cleared, not because the rule could not bring itself to let go.
        var rule = new NumberRule { Sequence = new SequenceSettings { Start = 1, Padding = 2 } };

        Assert.AreEqual("03.png", Fixture.Apply(rule, "shape 561@2x.png", Fixture.Context("shape 561@2x.png", index: 2)));
    }

    [TestMethod]
    public void NumberRule_TreatsTheSeparatorAsAPrefixWhileReplacing()
    {
        // Position has nothing left to arrange, so the separator leads whatever it is set to.
        var rule = new NumberRule
        {
            Position = InsertPosition.Suffix,
            Separator = "img-",
            Sequence = new SequenceSettings { Start = 1, Padding = 2 }
        };

        Assert.AreEqual("img-71.png", Fixture.Apply(rule, "shape 561@2x.png", Fixture.Context("shape 561@2x.png", index: 70)));
    }

    [TestMethod]
    public void NumberRule_ReplacingAFolderNameLeavesNoExtensionBehind()
    {
        // A folder called "v1.2" has no extension to keep, so replacing takes the whole thing.
        var context = Fixture.Context("v1.2", isDirectory: true);
        var rule = new NumberRule { Sequence = new SequenceSettings { Start = 4, Padding = 2 } };

        Assert.AreEqual("04", Fixture.Apply(rule, "v1.2", context));
    }

    [TestMethod]
    public void CleanupRule_RunsItsStepsInAFixedOrder()
    {
        var rule = new CleanupRule
        {
            RemoveDiacritics = true,
            CollapseWhitespace = true,
            ReplaceSpaces = false,
            StripInvalidCharacters = true,
            TrimEnds = true
        };

        Assert.AreEqual("cafe au lait.txt", Fixture.Apply(rule, "  café   au   lait  .txt"));
    }

    [TestMethod]
    public void CleanupRule_StripsCharactersWindowsRejects()
    {
        var rule = new CleanupRule { StripInvalidCharacters = true, InvalidReplacement = "-" };

        Assert.AreEqual("a-b-c.txt", Fixture.Apply(rule, "a:b|c.txt"));
    }

    [TestMethod]
    public void CleanupRule_DoesNotLeaveATrailingDotBehindAfterTruncating()
    {
        // Windows silently drops a trailing dot, so a truncation that produces one would rename the
        // file to something other than what the preview promised.
        var rule = new CleanupRule { MaxLength = 8, TrimEnds = true };

        Assert.AreEqual("report.txt", Fixture.Apply(rule, "report. and more.txt"));
    }

    [TestMethod]
    public void Rules_RaisePropertyChangedSoThePreviewCanRebuild()
    {
        var rule = new PatternRule();
        var raised = new List<string?>();
        rule.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        rule.Pattern = "{name}-x";
        rule.Sequence.Padding = 4;

        CollectionAssert.Contains(raised, nameof(PatternRule.Pattern));
        CollectionAssert.Contains(raised, nameof(PatternRule.Sequence));
    }

    [TestMethod]
    public void Clone_ProducesAnIndependentCopy()
    {
        var original = new PatternRule { Pattern = "{name}", IsEnabled = false };
        original.Sequence.Start = 5;

        var copy = (PatternRule)original.Clone();
        copy.Pattern = "changed";
        copy.Sequence.Start = 9;

        Assert.AreEqual("{name}", original.Pattern);
        Assert.AreEqual(5, original.Sequence.Start);
        Assert.IsFalse(copy.IsEnabled, "the enabled flag is part of the rule and should be copied");
    }
}
