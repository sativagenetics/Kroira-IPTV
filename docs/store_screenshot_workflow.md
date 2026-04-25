# Store Screenshot Workflow

Use `scripts/store-screenshots.ps1` only after preparing a clean, sanitized data set. The script launches the packaged app and captures the same visual routes used by the smoke workflow, but it cannot remove private or third-party content from the app database.

KROIRA starts empty until a user adds an authorized source. Store screenshots must show only sample or licensed source data that you are allowed to publish.

## Store-Safe Data Requirements

Public Store screenshots must not show:

- private playlist URLs, credentials, MAC addresses, or source names
- real provider brands or channel logos unless you have explicit rights
- copyrighted movie, series, or poster artwork unless you have explicit rights
- personal profile names, local test names, or account-like identifiers
- external notification overlays or Visual Studio/debug chrome

## Recommended Capture Setup

1. Back up any real KROIRA data you need to keep.
2. Use a clean Windows test account or clear the packaged app's local data for the test account.
3. Import only a sanitized M3U, Xtream, or supported portal source that you control.
4. Use sample titles, generic channel names, and artwork you own or are licensed to use.
5. Verify Home, Movies, Series, Live TV, Continue Watching, Favorites, Sources, Settings, Profile, and Player manually before capture.
6. Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\store-screenshots.ps1 -Configuration Release -Platform x64 -SanitizedDataConfirmed
```

7. Inspect every PNG before uploading to Partner Center.

Generated captures are written under `artifacts/store-screenshots` and are ignored by git.

## Content Disclaimer

KROIRA IPTV does not provide channels, streams, playlists, subscriptions, credentials, or media content. Store screenshots must use authorized sample data and must not imply that KROIRA provides free TV, bundled channels, premium content access, or DRM/paywall/authentication bypass.
