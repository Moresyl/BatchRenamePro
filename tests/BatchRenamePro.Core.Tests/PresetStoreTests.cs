using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Planning;
using BatchRenamePro.Core.Presets;
using BatchRenamePro.Core.Rules;
using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class PresetStoreTests
{
    private static RulePreset SamplePreset() => new(
        "我的预设",
        [
            new PatternRule { Pattern = "{modified:yyyy-MM-dd}_{name}", Scope = RenameScope.BaseName },
            new ReplaceRule { Find = @"\s+", ReplaceWith = "-", UseRegex = true },
            new CaseRule { Mode = CaseMode.Lower },
            new NumberRule { Position = InsertPosition.Suffix, Sequence = new SequenceSettings { Start = 5, Padding = 4, Style = SequenceStyle.Base36 } },
            new CleanupRule { RemoveDiacritics = true, MaxLength = 60 },
            new ExtensionRule { Mode = ExtensionMode.Replace, NewExtension = "md" },
            new RemoveRule { Mode = RemoveMode.Digits },
            new InsertRule { Text = "x", Position = InsertPosition.AtIndex, Index = 3 }
        ],
        ConflictPolicy.AutoNumber,
        Description: "round trip");

    [TestMethod]
    public async Task SaveAndLoad_RoundTripsEveryRuleType()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonPresetStore(directory.Path);
        var original = SamplePreset();

        await store.SaveAsync(original);
        var loaded = (await store.LoadAsync()).Single();

        Assert.AreEqual(original.Name, loaded.Name);
        Assert.AreEqual(ConflictPolicy.AutoNumber, loaded.ConflictPolicy);
        CollectionAssert.AreEqual(
            original.Rules.Select(rule => rule.TypeKey).ToArray(),
            loaded.Rules.Select(rule => rule.TypeKey).ToArray());

        var pattern = (PatternRule)loaded.Rules[0];
        Assert.AreEqual("{modified:yyyy-MM-dd}_{name}", pattern.Pattern);

        var number = (NumberRule)loaded.Rules[3];
        Assert.AreEqual(5, number.Sequence.Start);
        Assert.AreEqual(4, number.Sequence.Padding);
        Assert.AreEqual(SequenceStyle.Base36, number.Sequence.Style);

        var cleanup = (CleanupRule)loaded.Rules[4];
        Assert.IsTrue(cleanup.RemoveDiacritics);
        Assert.AreEqual(60, cleanup.MaxLength);
    }

    [TestMethod]
    public async Task SavedFile_KeepsNonAsciiTextReadable()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonPresetStore(directory.Path);

        await store.SaveAsync(SamplePreset());

        var json = await File.ReadAllTextAsync(Directory.GetFiles(directory.Path).Single());
        StringAssert.Contains(json, "我的预设", "a preset is meant to be shareable and hand-editable");
    }

    [TestMethod]
    public async Task ExportAndImport_MovesAPresetBetweenLocations()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonPresetStore(Path.Combine(directory.Path, "store"));
        var exportPath = Path.Combine(directory.Path, "shared.preset.json");

        await store.ExportAsync(SamplePreset(), exportPath);
        var imported = await store.ImportAsync(exportPath);

        Assert.AreEqual("我的预设", imported.Name);
        Assert.IsFalse(imported.IsBuiltIn, "an imported preset must be editable");
    }

    [TestMethod]
    public async Task Delete_RemovesAPreset()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonPresetStore(directory.Path);
        await store.SaveAsync(SamplePreset());

        await store.DeleteAsync("我的预设");

        Assert.IsEmpty(await store.LoadAsync());
    }

    [TestMethod]
    public async Task Save_RefusesToOverwriteABuiltIn()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonPresetStore(directory.Path);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.SaveAsync(BuiltInPresets.All[0]));
    }

    [TestMethod]
    public async Task Load_SkipsAFileItCannotUnderstand()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonPresetStore(directory.Path);
        await store.SaveAsync(SamplePreset());
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "broken.preset.json"), "{ not json");

        // One bad file — hand-edited, or written by a newer version — must not hide the rest.
        Assert.HasCount(1, await store.LoadAsync());
    }

    [TestMethod]
    public void BuiltInPresets_AreUsableAsIs()
    {
        Assert.IsNotEmpty(BuiltInPresets.All);

        foreach (var preset in BuiltInPresets.All)
        {
            Assert.IsTrue(preset.IsBuiltIn, preset.Name);
            Assert.IsNotEmpty(preset.Rules, preset.Name);
            Assert.AreEqual(0, preset.Rules.SelectMany(rule => rule.Validate()).Count(d => d.Severity == DiagnosticSeverity.Error), preset.Name);
            Assert.IsNotNull(BuiltInPresets.Find(preset.Name));
        }
    }

    [TestMethod]
    public void DeepCopy_DoesNotShareRuleInstances()
    {
        var preset = SamplePreset();

        var copy = preset.DeepCopy();
        ((PatternRule)copy.Rules[0]).Pattern = "changed";

        Assert.AreEqual("{modified:yyyy-MM-dd}_{name}", ((PatternRule)preset.Rules[0]).Pattern);
    }
}
