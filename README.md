<div align="center">

<img src="src/BatchRenamePro.App/Assets/app-256.png" width="112" alt="">

# Batch Rename Pro

**A batch file renamer for Windows that shows you exactly what it is about to do — and can undo it afterwards.**

[![CI](https://github.com/batchrenamepro/batchrenamepro/actions/workflows/ci.yml/badge.svg)](https://github.com/batchrenamepro/batchrenamepro/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/batchrenamepro/batchrenamepro?include_prereleases&sort=semver)](https://github.com/batchrenamepro/batchrenamepro/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/platform-Windows%2010%201809%2B-0078D4.svg)](#requirements)

[English](README.md) · [简体中文](README.zh-CN.md)

</div>

---

Renaming a folder full of files is the kind of task where a mistake is expensive and invisible: you
find out three weeks later that half of them lost their extension. Batch Rename Pro is built around
that risk. Every rule you add updates a live preview with the old and new name side by side and the
changed characters highlighted; anything unsafe is blocked *before* the first file moves; and what
does run is a two-phase transaction that rolls itself back if any step fails.

## Highlights

| | |
|---|---|
| **Composable rules** | Eight rule types stack into a pipeline. Each one sees the output of the last, so "strip the download suffix, then title-case it, then number it" is three cards, not three separate passes. |
| **Live preview with diffs** | Old name → new name for every item, with insertions and deletions highlighted and per-item status (unchanged, renamed, blocked, conflicting). |
| **Nothing runs unchecked** | Reserved device names, illegal characters, trailing dots, path-length overruns, duplicate targets, collisions with files already on disk — all caught in planning, all surfaced as problems you can read. |
| **Transactional execution with undo** | Renames go through a staging phase so a failure halfway through restores every name it already changed. Completed runs stay in the history and can be undone later. |
| **Presets** | Six built-in pipelines for the jobs people actually open a rename tool to do, plus your own, saved as JSON. |
| **Tokens** | 22 placeholders — name, extension, parent folder, index, total, size, created/modified dates with custom formats, GUID, random suffix — with an in-app reference. |
| **Bilingual and accessible** | Ships zh-CN and en-US, switchable at runtime with no restart. Every interactive element is named for screen readers, verified by an automated UI Automation sweep. |
| **Modern Windows shell** | Custom title bar, Mica/Acrylic backdrop, light/dark/system theming, Per-Monitor V2 DPI, rounded corners on Windows 11 with automatic fallback on Windows 10. |
| **Private by construction** | No telemetry, no network calls, no elevation. History and logs are plain files under `%LOCALAPPDATA%`. |

## Install

Download the archive for your machine from the [latest release](https://github.com/batchrenamepro/batchrenamepro/releases/latest),
unzip it anywhere, and run `BatchRenamePro.exe`. The builds are self-contained — no .NET install
required.

| Download | For |
|---|---|
| `BatchRenamePro-win-x64.zip` | Almost every PC and laptop |
| `BatchRenamePro-win-arm64.zip` | Snapdragon / ARM64 devices |
| `BatchRenamePro-win-x86.zip` | 32-bit Windows |

Verify what you downloaded against `SHA256SUMS.txt` in the same release:

```powershell
Get-FileHash .\BatchRenamePro-win-x64.zip -Algorithm SHA256
```

## Quick start

1. **Add files** — drag them onto the list, or use *Files* / *Folder*.
2. **Add a rule** — press *Add rule* and pick one. It appears as a card you can expand, reorder, disable or delete.
3. **Read the preview** — the right-hand pane updates as you type. Red rows are blocked; the problems strip says why.
4. **Run it** — *Start renaming*. If anything goes wrong the whole batch is rolled back.
5. **Change your mind** — *Undo* on the run, or the History page later.

### The rules

| Rule | What it does |
|---|---|
| **Pattern** | Rebuilds the name from a template: `{modified:yyyy-MM-dd}_{name}_{index:000}`. Also accepts the classic `*` (original name) and `#` (counter). |
| **Replace** | Find and replace, literal or regular expression, optionally case-insensitive. |
| **Number** | Sequential numbering as a prefix or suffix — start, step, zero-padding, group size, digits or letters. |
| **Insert** | Puts text at the front, at the back, or at a character position. |
| **Remove** | Deletes a character range, or every digit, symbol or space. |
| **Case** | UPPER, lower, Title Case, Sentence case. |
| **Extension** | Change, add, remove or normalise the extension's case. |
| **Cleanup** | Collapse runs of whitespace, trim the ends, strip accents, swap spaces for a separator, drop characters Windows will not accept. |

Every rule has a **scope**: apply it to the base name only, the extension only, or the whole name.
That one switch is why the pipeline stays short — you rarely need a second rule to protect the
extension from the first.

### Handling clashes

When two items would end up with the same name, or the name is already taken on disk, the
conflict policy decides:

- **Block** — refuse to run, and show which rows collide. The default when you cannot afford a surprise.
- **Auto-number** — append ` (2)`, ` (3)` … deterministically, so the same input always produces the same output.
- **Skip** — leave the colliding items alone and rename the rest.

## Requirements

- Windows 10 version 1809 (build 17763) or later, or Windows 11
- x64, ARM64 or x86
- No .NET runtime needed for release builds; the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source

## Build from source

```powershell
git clone https://github.com/batchrenamepro/batchrenamepro.git
cd batchrenamepro
dotnet restore BatchRenamePro.sln
dotnet build BatchRenamePro.sln -c Release
dotnet test  BatchRenamePro.sln -c Release
dotnet run   --project src\BatchRenamePro.App
```

Producing the same self-contained single file the releases ship:

```powershell
dotnet publish src\BatchRenamePro.App\BatchRenamePro.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

> The build treats warnings as errors and runs the .NET analyzers at `latest-recommended`. That is
> deliberate: a green build is a meaningful signal, so keep it green rather than suppressing it.

## Architecture

```
BatchRenamePro.Core        no UI, no Windows dependency, fully unit-tested
├── Abstractions/          IRenameRule, RenameContext, NameParts, scopes, diagnostics
├── Rules/                 the eight rules — each one pure: (name, context) → name
├── Tokens/                the token engine and sequence formatter
├── Planning/              the planner: runs the pipeline, validates, detects conflicts
├── Execution/             two-phase transactional rename, undo, JSON history
├── Scanning/              file and folder enumeration
├── Sorting/               natural (explorer-like) ordering
└── Presets/               built-in pipelines and JSON persistence

BatchRenamePro.App         WPF, MVVM, dependency-injected
├── ViewModels/            one per page, plus the rule-card and token-picker models
├── Views/                 shell + five pages
├── Themes/                design tokens, palettes, control styles, rule editors
├── Controls/              diff-rendering text, pattern editor with token picker
├── Localization/          the string catalog, {loc:T} markup extension, enum sources
├── Services/              settings, theming, dialogs, notifications, file logging
└── Interop/               DWM window frame — Mica, dark title bar, rounded corners
```

Two rules keep the layers honest:

1. **Core never references WPF.** It targets plain `net10.0` and is the only project with tests.
   Anything that can be decided without a window is decided there.
2. **The planner is the single gate.** Rules never touch the file system; they transform strings.
   Validation, conflict detection and ordering all happen in one place, so the preview and the
   execution can never disagree about what is going to happen.

Adding a rule means implementing `IRenameRule`, registering it in `RuleCatalog`, adding an editor
`DataTemplate`, and adding the strings. `CONTRIBUTING.md` walks through it.

## Data and privacy

The app runs as a normal user, never elevates, and makes no network requests of any kind.

Everything it writes lives in one folder, `%APPDATA%\BatchRenamePro`:

| What | Where |
|---|---|
| Settings | `settings.json` |
| Presets you save | `Presets\` |
| Rename history (for undo) | `History\` |
| Logs | `Logs\` |

Deleting that folder resets the app completely. History is capped and prunes itself; the limit is on
the Settings page.

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for the build, the
coding conventions and how to add a rule, and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for how we
work together. Security reports go through [SECURITY.md](SECURITY.md), not the public issue tracker.

Good first contributions: a new rule, a new built-in preset, a translation, or a test that pins
down behaviour that is currently only implied.

## License

[MIT](LICENSE) © Batch Rename Pro Contributors.

Inspired by the three-tab rename workflow of the classic Windows archivers, rebuilt from scratch on
.NET 10 with a preview you can trust.
