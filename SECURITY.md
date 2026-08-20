# Security Policy

## Supported versions

Security fixes go into the latest release. Older versions are not patched — the upgrade path is to
download the current build, since the application is a self-contained executable with no installer
state to migrate.

| Version | Supported |
|---|---|
| 2.x | ✅ |
| < 2.0 | ❌ |

## Reporting a vulnerability

**Please do not open a public issue.**

Report privately through GitHub's
[private vulnerability reporting](https://github.com/Moresyl/BatchRenamePro/security/advisories/new)
on this repository. That gives us a private thread with you and a way to credit you when the advisory
is published.

Helpful things to include, in rough order of usefulness:

- What an attacker gains, concretely — a file overwritten outside the selected folder, code executed,
  something read that should not be readable.
- The steps to reproduce it: the rules configured, the file names involved, the folder layout.
- The version from the About page and your Windows version.
- Whether it needs the user to do something unusual, or happens during a normal rename.

You can expect an acknowledgement within **3 working days** and an assessment within **10**. If a fix
is warranted we will agree a disclosure date with you — typically when the fix ships, and no later
than 90 days. We will credit you in the advisory and the changelog unless you would rather stay
anonymous.

## Threat model

Worth stating plainly, because it decides what counts as a vulnerability here.

The application runs as a normal user, never requests elevation, has no telemetry and has no server
component. Its only optional network request is an anonymous read of the configured public GitHub
repository's latest Release metadata. It never downloads or executes an update, and it uploads no
file names, history, presets, logs, identifiers or usage data. Everything it writes lives under
`%APPDATA%\BatchRenamePro`.

The untrusted inputs are therefore:

- **File and folder names**, including names crafted by someone else — long ones, ones with reserved
  device names, control characters, right-to-left overrides, path separators, or Unicode that
  normalises into something different.
- **Preset files** (`Presets\*.json`), which a user might be handed by someone else.
- **History files** (`History\*.json`) used to drive undo.
- **Rule input**, notably regular expressions, which can be written to backtrack catastrophically.
- **GitHub Release metadata**, treated as untrusted display text. The browser destination is rebuilt
  locally from the configured repository and returned tag rather than trusting an arbitrary URL in
  the response.

### In scope

- Renaming, moving, overwriting or deleting anything outside the items the user selected.
- Getting the planner and the executor to disagree — anything where what runs differs from what the
  preview showed.
- Losing data on a failure path: a rollback that does not restore, or an undo that hits the wrong file.
- A malicious preset or history file causing file writes outside the data directory, code execution,
  or deserialization of unexpected types.
- Denial of service from a name or pattern that is realistic to encounter — a hang or unbounded
  memory growth on ordinary input.

### Out of scope

- The user deliberately renaming their own files into a mess. The preview showed them; undo is there.
- Needing local administrator rights, or write access to the data directory, to trigger the issue.
  Anyone with that access does not need this application.
- Anything requiring a modified build, an attached debugger, or patched binaries.
- Antivirus false positives on the unsigned executable. Release builds are unsigned; verify them
  against the published `SHA256SUMS.txt`.
- Missing hardening flags that do not enable a concrete attack. Tell us anyway — as an issue, not an
  advisory — we would like to fix them, just not under embargo.

## What we do on our side

The dependency set is deliberately small (`CommunityToolkit.Mvvm` and `Microsoft.Extensions.*`),
central package management keeps every version in one file, and Dependabot watches both NuGet and the
GitHub Actions we use. Builds treat warnings as errors with .NET analysis at `latest-recommended`, and
release builds are deterministic so a build can be reproduced from a tag.
