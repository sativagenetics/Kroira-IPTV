# Microsoft Store Release Checklist

## 1. V2 Identity

- Confirm the package manifest identity version is `2.0.0.0`.
- Confirm app assembly/package metadata is `2.0.0`.
- Keep the app name consistent with the package manifest unless Partner Center copy is intentionally changed. Current app display name is `KROIRA IPTV`.
- Verify publisher identity and Partner Center package identity before uploading release artifacts.

## 2. Capabilities

Because the V2 baseline targets Windows desktop playback, the app must not ask for unnecessary permissions.

- Required capabilities:
  - `internetClient` for HTTP/HLS/MPEG-TS streaming and Store receipt flows.
  - `runFullTrust` for the packaged Windows App SDK / WinUI 3 entrypoint token used by this app.
- Do not add `privateNetworkClientServer`, broad filesystem access, or `backgroundMediaPlayback` unless that scope is separately productized and validated.

## 3. Validation Gate

Run the baseline validation before packaging:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-release.ps1 -Configuration Release
```

If packaging is needed locally and unsigned MSIX output is acceptable for review, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-unsigned.ps1 -Configuration Release -Platform x64
```

Do not upload unsigned packages to Partner Center.

## 4. Store Copy And Content Policy

The Store description must clearly state:

> KROIRA IPTV is a media player utility. Please supply your own legally acquired content. No streams, subscriptions, or channels are provided.

The listing must not imply that KROIRA supplies channels, playlists, accounts, credentials, subscriptions, provider access, or copyrighted content.

## 5. Screenshots And Assets

- Capture screenshots only from sanitized/sample source data that you own or have permission to show.
- Do not publish screenshots that expose private playlist URLs, credentials, MAC addresses, personal profile names, third-party provider brands, channel logos, copyrighted posters, or debug tooling.
- Use `scripts/store-screenshots.ps1` with `-SanitizedDataConfirmed` only after manually confirming the data set is store-safe.
- Follow `docs/store_screenshot_workflow.md` and `docs/screenshots/README.md`.
- Generated screenshots belong under `artifacts/store-screenshots` and are ignored by git.

## 6. Privacy And Support

- Link the Store listing to the published privacy policy.
- The privacy copy must state that KROIRA stores source settings, credentials, favorites, playback progress, and preferences locally on the device.
- Confirm the support URL and support email are current before submission.

## 7. Monetization Metadata

- Configure the application listing in Partner Center as `Free` unless the release plan explicitly changes.
- Do not publish paid add-ons or in-app purchase SKUs unless entitlement behavior and Store receipt validation have been separately validated for the release.
