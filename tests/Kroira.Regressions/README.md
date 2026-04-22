# KROIRA Regression Corpus

This project runs deterministic end-to-end regression coverage against the real KROIRA ingestion pipeline: source refresh, parsing, enrichment, source health, probing, catchup, and operational mirror selection.

## What it covers

- M3U import and sync behavior
- Xtream live/VOD/series sync behavior
- XMLTV parsing and guide attachment
- live/movie/series classification
- normalization and logical identity behavior
- EPG and logo fallback
- source health components and bounded probe interpretation
- catchup detection
- operational candidate ranking and recovery-sensitive outcomes

## Layout

Each case lives under `Corpus/<case-id>/`.

- `case.json`: case definition and source inputs
- `expected.json`: committed baseline snapshot
- `server.json`: optional local HTTP fixture routing manifest
- payload files: `.m3u`, `.xml`, `.json`, `.txt`, or other minimized fixture assets

Generated outputs are written to `artifacts/` and are intentionally ignored by git.

- `<case-id>.actual.json`: latest actual snapshot for diffing
- `<case-id>.db`: temporary SQLite database used during the run

## Running locally

From the repo root:

```powershell
./scripts/run-regressions.cmd
```

Useful options:

```powershell
./scripts/run-regressions.cmd --list
./scripts/run-regressions.cmd --case m3u_full_pipeline
./scripts/run-regressions.cmd --update
```

You can also run the project directly:

```powershell
dotnet run --project tests/Kroira.Regressions --property:Platform=x64 -- --case xtream_full_pipeline
```

If you prefer the PowerShell script directly, run it with execution-policy bypass:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-regressions.ps1 --case xtream_full_pipeline
```

## Baseline workflow

- Run in verify mode by default. The runner compares each case against `expected.json`.
- When behavior changes intentionally, rerun with `--update`.
- Review the updated `expected.json` diff before committing. The runner keeps the last actual output in `artifacts/` to make failures readable.

## Adding a new case

1. Create `Corpus/<new-case-id>/`.
2. Add a `case.json` with one or more sources.
3. Add only the minimized payload files needed for that case.
4. Add `server.json` if the pipeline needs deterministic HTTP responses, delays, or status-code variations.
5. Run `./scripts/run-regressions.cmd --case <new-case-id> --update`.
6. Inspect the generated `expected.json`, then rerun without `--update`.

## Fixture design rules

- Prefer reduced real samples when safe; anonymize provider details.
- Keep payloads minimal but representative of the bug or behavior under test.
- Stub network behavior through `server.json`; do not depend on live providers.
- Add cases when a new provider quirk or regression is discovered so the corpus keeps getting sharper.
