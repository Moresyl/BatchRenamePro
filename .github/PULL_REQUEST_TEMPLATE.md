<!--
  Thanks for the pull request. Fill in what applies and delete what does not —
  a two-line PR that fixes a typo does not need the whole form.
-->

## What this changes

<!-- One or two sentences. -->

## Why

<!-- The reasoning, when it is not obvious from the change. Link the issue: Fixes #123 -->

## How you convinced yourself it works

<!--
  The tests you added, the cases you tried by hand, the file names you tried it against.
  For anything in Core, a test is expected.
-->

## Screenshots

<!-- Required for UI changes. Both themes if you touched colours; both languages if you touched text. -->

---

- [ ] `dotnet build BatchRenamePro.sln -c Release` is clean — warnings are errors here
- [ ] `dotnet test BatchRenamePro.sln -c Release` passes
- [ ] `dotnet format BatchRenamePro.sln --verify-no-changes` passes
- [ ] Core changes have tests
- [ ] New user-facing text is in **both** language tables in `StringCatalog.cs`, not literal in XAML
- [ ] New interactive controls have an accessible name, and the keyboard reaches everything the mouse can
- [ ] `CHANGELOG.md` updated under `## [Unreleased]`, if this is user-visible

### Does this affect saved data?

<!--
  Answer if you touched rules, presets or history. Rule type keys and the preset JSON shape are an
  on-disk format: say what happens to presets people have already saved.
-->
