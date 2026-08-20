using BatchRenamePro.App.Localization;

namespace BatchRenamePro.App.Tests;

/// <summary>
/// Guards the two shipped languages against drifting apart.
/// </summary>
/// <remarks>
/// A missing translation does not fail loudly. <see cref="StringCatalog.Get" /> falls back to English,
/// so the app keeps working and the only symptom is an English sentence sitting in a Chinese window —
/// which nobody notices until a user does. These tests are the thing that notices.
/// </remarks>
[TestClass]
public sealed class StringCatalogTests
{
    [TestMethod]
    public void EveryLanguageOffered_HasATableOfItsOwn()
    {
        foreach (var language in StringCatalog.Languages)
            Assert.IsTrue(StringCatalog.IsSupported(language.Culture), $"{language.Culture} is offered in settings but has no table.");
    }

    [TestMethod]
    public void EveryTable_HasATableOfItsOwnOfferedInSettings()
    {
        // The other direction: a table nobody can select is dead weight, and usually means a language
        // was half-added and the settings list was the half that got forgotten.
        foreach (var culture in StringCatalog.All.Keys)
            Assert.IsTrue(
                StringCatalog.Languages.Any(language => string.Equals(language.Culture, culture, StringComparison.OrdinalIgnoreCase)),
                $"{culture} has a table but is not offered in settings.");
    }

    [TestMethod]
    public void AllLanguages_TranslateExactlyTheSameKeys()
    {
        var reference = StringCatalog.All[StringCatalog.FallbackCulture].Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var (culture, table) in StringCatalog.All)
        {
            if (culture == StringCatalog.FallbackCulture) continue;

            var keys = table.Keys.ToHashSet(StringComparer.Ordinal);

            var untranslated = reference.Except(keys).Order(StringComparer.Ordinal).ToList();
            var orphaned = keys.Except(reference).Order(StringComparer.Ordinal).ToList();

            Assert.IsEmpty(untranslated, $"{culture} is missing: {string.Join(", ", untranslated)}");
            Assert.IsEmpty(orphaned, $"{culture} translates keys no other language has: {string.Join(", ", orphaned)}");
        }
    }

    [TestMethod]
    public void NoTranslation_IsBlank()
    {
        // A key present but empty reads as a bug in the layout rather than a bug in the table, which
        // is the more expensive of the two to chase.
        foreach (var (culture, table) in StringCatalog.All)
            foreach (var (key, value) in table)
                Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{culture}/{key} is blank.");
    }

    [TestMethod]
    public void FormatPlaceholders_MatchAcrossLanguages()
    {
        // "已选 {0} 项" and "{0} of {1} selected" would both compile and both look fine in review, and
        // the second one throws the moment it is formatted with a single argument.
        var reference = StringCatalog.All[StringCatalog.FallbackCulture];

        foreach (var (culture, table) in StringCatalog.All)
        {
            if (culture == StringCatalog.FallbackCulture) continue;

            foreach (var (key, value) in table)
            {
                if (!reference.TryGetValue(key, out var english)) continue;

                Assert.AreEqual(
                    Placeholders(english),
                    Placeholders(value),
                    $"{culture}/{key} uses different placeholders from the English text.");
            }
        }
    }

    [TestMethod]
    public void EveryEnumWithLabels_LabelsAllOfItsValues()
    {
        // Combo boxes take their options from EnumOptions, which builds the key "enum.Type.Value" from
        // the value itself. So adding a value to an enumeration silently adds an option whose label is
        // the key, and renaming one silently orphans the label it had — both of which reach the screen
        // looking like "enum.RemoveMode.Symbols" sitting in a drop-down.
        var labels = StringCatalog.All[StringCatalog.FallbackCulture].Keys
            .Where(key => key.StartsWith("enum.", StringComparison.Ordinal))
            .Select(key => key.Split('.'))
            .Where(parts => parts.Length is 3)
            .GroupBy(parts => parts[1], parts => parts[2], StringComparer.Ordinal);

        foreach (var group in labels)
        {
            var type = EnumNamed(group.Key);
            Assert.IsNotNull(type, $"enum.{group.Key}.* is translated, but no such enumeration exists.");

            var values = Enum.GetNames(type).ToHashSet(StringComparer.Ordinal);
            var labelled = group.ToHashSet(StringComparer.Ordinal);

            Assert.IsEmpty(
                values.Except(labelled).Order(StringComparer.Ordinal).ToList(),
                $"{group.Key} has values with no label, which a combo box shows as their key.");
            Assert.IsEmpty(
                labelled.Except(values).Order(StringComparer.Ordinal).ToList(),
                $"{group.Key} is translated for values it no longer has.");
        }
    }

    private static Type? EnumNamed(string name) =>
        new[] { typeof(StringCatalog).Assembly, typeof(Core.Scanning.ScanTarget).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .FirstOrDefault(type => type.IsEnum && string.Equals(type.Name, name, StringComparison.Ordinal));

    // Sorted and de-duplicated: the languages are allowed to put {0} and {1} in whatever order the
    // grammar wants, and to repeat one. What they may not do is disagree about which exist.
    private static string Placeholders(string text) =>
        string.Join(
            ",",
            System.Text.RegularExpressions.Regex.Matches(text, @"\{(\d+)[^}]*\}")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
}
