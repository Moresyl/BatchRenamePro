# Contributing

Thanks for taking the time. This file covers what you need to get a change merged: the build, the
conventions the code already follows, and the two or three walkthroughs that save the most time.

Issues, discussion and pull requests are all welcome in **English or 中文** — write in whichever you
think in. Code comments and identifiers are English so that the codebase reads consistently.

## Getting set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Windows 10 1809 or later.
`global.json` pins the SDK band, so `dotnet` will tell you plainly if yours is too old.

```powershell
git clone https://github.com/batchrenamepro/batchrenamepro.git
cd batchrenamepro
dotnet restore BatchRenamePro.sln
dotnet build   BatchRenamePro.sln -c Release
dotnet test    BatchRenamePro.sln -c Release
dotnet run     --project src\BatchRenamePro.App
```

Any editor works. Visual Studio 2022+, Rider and VS Code with the C# Dev Kit all open the solution
directly; `.editorconfig` carries the formatting rules, so let your editor apply it rather than
hand-formatting.

### Before you push

```powershell
dotnet format BatchRenamePro.sln --verify-no-changes
dotnet build  BatchRenamePro.sln -c Release
dotnet test   BatchRenamePro.sln -c Release
```

CI runs exactly this. `TreatWarningsAsErrors` is on and analysis is at `latest-recommended`, so a
warning is a build failure. Fix the cause; reach for a suppression only when the analyzer is
genuinely wrong, and then leave a comment saying why.

## How the code is laid out

```
src/BatchRenamePro.Core   the engine — no UI, no Windows dependency, all the tests
src/BatchRenamePro.App    the WPF application — MVVM, dependency-injected
tests/…Core.Tests         MSTest, one file per unit under test
```

Two boundaries matter more than any style rule:

**Core never references WPF, and never asks where it is running.** It targets plain `net10.0`.
Anything decidable without a window is decided there, which is why almost every behavioural test can
be written without launching an application. Stores are *told* their directory rather than reading
`%APPDATA%` themselves — that is what lets a test point them at a temporary folder.

**Rules do not touch the file system.** A rule is a function from a name to a name. The planner is
the single place that validates, detects conflicts and orders the work, so the preview and the
execution physically cannot disagree about what is about to happen. If you find yourself wanting to
call `File.Exists` inside a rule, the answer belongs in the planner instead.

## Adding a rename rule

The eight existing rules are the best reference; `CleanupRule` is the most representative. The shape:

1. **The rule** — `src/BatchRenamePro.Core/Rules/YourRule.cs`. Derive from `RenameRuleBase`, give it
   a `const string Key` (a stable discriminator that outlives renames of the class), and implement
   `Apply`. Honour `Scope`: the base class hands you the part you are allowed to change. Report
   anything doubtful through `RuleDiagnostic` rather than throwing — a bad regular expression should
   show up as a warning in the preview, not as a crash.
2. **Serialization** — add a `[JsonDerivedType]` line to `RenameRuleBase` so presets round-trip.
   Unknown types deliberately fail serialization rather than silently vanishing, so forgetting this
   step shows up immediately. The `Key` is what gets written to disk; changing it later breaks
   everyone's saved presets.
3. **Tests** — `tests/BatchRenamePro.Core.Tests/RuleTests.cs`. Cover the empty name, a name that is
   all extension, a Unicode name, and the case where the rule should do nothing at all.
4. **The menu entry** — one line in `src/BatchRenamePro.App/ViewModels/RuleCatalog.cs`, placed by how
   often people will reach for it rather than alphabetically.
5. **The editor** — a `DataTemplate` in `src/BatchRenamePro.App/Themes/RuleEditors.xaml`. Copy the
   nearest existing one; it will already have the label/field pairing and the
   `AutomationProperties.LabeledBy` wiring you need.
6. **The strings** — `rule.yourrule.name` and `rule.yourrule.summary`, plus every field label, in
   **both** language tables in `src/BatchRenamePro.App/Localization/StringCatalog.cs`.
7. **The icon** — a `Geometry` in `Themes/Icons.xaml`, referenced by the catalog entry.

## Working on the UI

Some conventions that are load-bearing rather than cosmetic:

- **No literal user-facing text in XAML.** Use `{loc:T some.key}` and add the key to both tables. The
  markup extension returns a `Binding`, so it works on any dependency property — including attached
  ones like `AutomationProperties.Name` — and language changes take effect without a restart.
- **Colours and sizes come from the palettes**, via `DynamicResource`. A hard-coded brush will look
  wrong in the other theme, and nobody will notice until a screenshot arrives in an issue.
- **Everything interactive needs an accessible name.** Point `AutomationProperties.LabeledBy` at the
  visible label you already wrote — that way the accessible name cannot drift away from the UI or
  from the translation. For `ItemsControl`s, set it on an `ItemContainerStyle` targeting
  `ContentPresenter`. Any type bound into a combo box needs a `ToString()` override, or a screen
  reader will read out the whole record.
- **`StaticResource` inside a merged dictionary only sees itself and its own nested dictionaries** —
  never a sibling dictionary. Use `DynamicResource` across files.

If you change the UI, sweep it before you push:

```powershell
dotnet run --project src\BatchRenamePro.App
```

then walk every page you touched in both languages, in both themes, and confirm the keyboard reaches
everything the mouse can.

## Translations

Both languages live in `StringCatalog.cs` as plain dictionaries, so a new language is a third
dictionary plus an entry in the language list — no resource-file tooling involved. Keep the keys
identical across tables; a missing key falls back to the key itself, which is visible but ugly.

## Commits and pull requests

Write commit subjects in the imperative mood and under about 72 characters — `Add a rule that strips
diacritics`, not `added stuff`. The body is for *why*, when why is not obvious.

A pull request wants: what changed, why, and how you convinced yourself it works. Screenshots for
anything visual. If it changes rename behaviour, say what happens to presets people have already
saved. Small and focused beats large and comprehensive — two clean PRs merge faster than one that
does both things.

Please make sure CI is green before asking for review, and add tests for anything in Core. A change
to Core without a test is the one thing likely to be sent back.

## Reporting bugs

Use the issue templates — they ask for the version from the About page (there is a copy button), your
Windows version, and the rule setup involved. A rename bug is almost always reproducible from *the
rules plus one or two example names*, so those two things are worth more than a long description.

Security issues do **not** go in the tracker. See [SECURITY.md](SECURITY.md).

## Code of conduct

Participating means agreeing to the [Code of Conduct](CODE_OF_CONDUCT.md). It is short, and it is
enforced.
