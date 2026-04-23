# Contributing to KROIRA IPTV

## Scope

Contributions should improve the current restored product baseline, not revive abandoned experiments.

Good contributions include:

- bug fixes
- reliability improvements
- documentation improvements
- focused UX polish that preserves the current direction
- regression coverage for real bugs or provider quirks

Out of scope for drive-by changes:

- redesign passes with no product alignment
- speculative rewrites of backend/service/model layers
- adding provider credentials, copyrighted fixtures, or temporary artifacts

## Before You Start

- Search existing issues before opening a new one.
- For larger changes, open an issue or start from an agreed bug/feature request first.
- Keep proposals aligned with the current player-only product position.

## Development Setup

Requirements:

- Windows 10 or Windows 11
- .NET 8 SDK
- Visual Studio 2022 or newer
- Windows App SDK compatible environment

Open `Kroira.sln`, select the packaged `Kroira.App` startup profile, and build for `x64`.

## Build and Validation

Build the solution:

```powershell
dotnet build Kroira.sln -c Debug -p:Platform=x64
```

Run the regression corpus:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-regressions.ps1
```

For CI-equivalent validation:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\ci-regressions.ps1 -Configuration Release
```

## Pull Requests

Keep pull requests focused and reviewable.

Expected for most PRs:

- a clear problem statement
- a concise summary of what changed
- updated docs when behavior changes
- build success
- regression coverage when logic changes justify it

If you change visible UI behavior, include sanitized screenshots only when they are stable enough to be useful. Repository-quality screenshots belong under `docs/screenshots/`. Temporary debug captures do not belong in git.

## Source and Privacy Rules

Never commit:

- live provider credentials
- personal playlist URLs
- exported customer data
- screenshots that expose private source information
- copyrighted stream assets unless they are clearly permitted and intentionally curated

Regression fixtures should be minimized, sanitized, and deterministic.

## Coding Expectations

- Preserve the current restored app behavior unless the change intentionally fixes it.
- Prefer small, well-scoped edits over broad rewrites.
- Keep docs honest to the current product state.
- Treat the working tree as the source of truth for active UI direction.

## Community Standards

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
