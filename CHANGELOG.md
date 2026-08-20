# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **A choice of folder picker.** Settings → Defaults → "Choose folders with" selects between the
  Windows folder dialog and a folder browser built into the application. The Windows dialog is the
  default; it is the one every other program opens, with the user's own pinned places in it. The
  in-app browser lists the files inside each folder as well as its subfolders, which is what the
  Windows dialog will not do.

### Changed

- **The window is a fixed size.** It cannot be dragged larger or smaller and cannot be maximized —
  the maximize button is gone, and so is the resize border. It still minimizes, and still minimizes
  to the notification area when that setting is on. A window that opens larger than the screen it
  landed on is shrunk to fit, since with no resize border there would otherwise be no way back.
- A window too small for its content is no longer possible, so the remembered window size and the
  maximized flag are no longer written to `settings.json`. Existing files keep the three keys until
  the next save, which drops them; nothing reads them in the meantime.

### Fixed

- The folder picker showed "no items match your search" when opened on a folder that contained files
  but no subfolders. The Windows folder dialog lists folders only, which is what that message
  reports — the app was seeing the files correctly the whole time. It is now explained where the
  picker is chosen, and the in-app browser avoids it entirely.
- A maximize command sent by something other than the user — a window manager, an accessibility
  tool, UI Automation's `SetWindowVisualState` — is now refused rather than obeyed. Windows honours
  an explicit `SC_MAXIMIZE` regardless of whether the window has a maximize box.

## [2.0.0] — 2026-08-20

A rewrite. Version 1 was a fixed three-tab form; this is a rule pipeline with a shell around it.
Presets and history from 1.x are not carried over — there was no on-disk format to carry.

### Added

- **Composable rule pipeline.** Eight rule types — pattern, replace, number, insert, remove, case,
  extension and cleanup — stack in any order, each one seeing the output of the last. Rules can be
  reordered, disabled individually, or collapsed out of the way.
- **Rule scope.** Every rule applies to the base name, the extension, or the whole name, which is
  what keeps a pipeline to two or three rules instead of five.
- **Token engine.** 22 placeholders covering name, extension, parent folder, index, total, file size,
  created and modified dates with custom format strings, GUID and random suffixes. Expansion is a
  single left-to-right scan, so a file literally named `report#2` keeps its `#`. Unknown tokens are
  emitted verbatim and reported as a warning rather than silently deleting part of the name.
- **Presets.** Six built-in pipelines (photos, sequential numbering, web-safe, date prefix, tidy
  downloads, lowercase extensions) and your own, saved as JSON with a versioned type discriminator.
- **Live preview with diffs.** Old name → new name per item with the changed characters highlighted,
  plus per-item status: unchanged, will rename, blocked, conflicting.
- **Conflict policies.** Block, auto-number or skip, chosen per run. Auto-numbering is deterministic:
  the same input always produces the same output.
- **Transactional execution.** A two-phase rename that rolls back every name it has already changed
  if any step fails, with progress reporting and cancellation.
- **History and undo.** Completed runs are recorded as JSON and can be undone after the fact, not
  just during the session. The history is capped and prunes itself; the limit is a setting.
- **Validation before anything moves.** Reserved device names, illegal characters, trailing dots and
  spaces, path-length overruns, duplicate targets and collisions with files already on disk are all
  caught during planning and shown as readable problems.
- **New shell.** Custom title bar, collapsible navigation rail and five pages — rename, presets,
  history, settings, about.
- **Theming.** Light, dark and follow-system, with Mica and Acrylic backdrops and rounded corners on
  Windows 11, degrading cleanly on Windows 10. Palettes are design tokens, so themes stay consistent.
- **Localization.** Simplified Chinese and English, switchable at runtime with no restart. Enum-backed
  combo boxes re-localize in place, including drop-downs that are already open.
- **Accessibility.** Every interactive element carries an accessible name, wired to the visible label
  where there is one so the two cannot drift apart. Verified by an automated UI Automation sweep
  across all five pages, all eight rule editors and both popups, in both languages.
- **Natural sorting**, matching Explorer's ordering, plus manual reordering.
- **Diagnostics.** File logging under the data directory, and a copy-diagnostics button on the About
  page that produces the version and environment lines a bug report needs.
- **GitHub Releases update centre.** Optional delayed startup checks, a title-bar notification,
  release notes and publication date on the About page, manual checks, persistent “ignore this
  version”, and one-click opening of the exact GitHub Release.
- **Hardened update channel.** Strict repository parsing, stable-release validation,
  semantic-version comparison, trusted release URL construction and request timeouts, isolated
  behind a separately testable service.

### Changed

- Rebuilt on **.NET 10** with MVVM (CommunityToolkit.Mvvm), dependency injection, and a Core library
  that has no UI or Windows dependency and holds all of the tests.
- The engine and the UI were split into `BatchRenamePro.Core` and `BatchRenamePro.App`. Rules no
  longer touch the file system; the planner is the only component that does, which is what makes the
  preview and the execution incapable of disagreeing.
- Central package management, deterministic release builds, warnings as errors and .NET analysis at
  `latest-recommended` across the whole solution.
- Numbering keeps its documented behaviour: padding is a *minimum* width, so a run of `01…99`
  continues into `100` rather than switching to `001`.
- Releases ship as one compressed, self-contained executable per runtime — x64, ARM64 and x86, about
  68 MB each, with no .NET install required. The publish settings live in the project file, so a
  local publish and the one CI runs produce the same binary.
- The privacy model permits one narrowly scoped, user-configurable request to GitHub's public
  Releases API. It sends no file names, history, presets, telemetry or usage data.

### Fixed

- Renaming into a name that differs only by case (`photo.JPG` → `photo.jpg`) no longer fails on the
  case-insensitive file system.
- Progress reporting no longer depends on the order callbacks arrive in, which made the last report
  of a batch non-deterministic.
- History is ordered deterministically. Entries saved within the same clock tick — the file-system
  timestamp only advances every 15ms or so — sorted arbitrarily, so the history page could show
  batches out of order and pruning could delete the wrong ones.

[Unreleased]: https://github.com/Moresyl/BatchRenamePro/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/Moresyl/BatchRenamePro/releases/tag/v2.0.0
