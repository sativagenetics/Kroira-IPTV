# Microsoft Store Release Checklist

## 1. V2 Identity

- Confirm the package manifest identity version is `2.0.0.0`.
- Confirm app assembly/package metadata is `2.0.0`.
- Confirm the Store release version is listed as `2.0.0`.
- Keep the app name consistent with the package manifest unless Partner Center copy is intentionally changed. Current app display name is `KROIRA IPTV`.
- Verify publisher identity and Partner Center package identity before uploading release artifacts.
- Keep `docs/store_submission_info.md`, the in-app Settings legal text, and Partner Center copy aligned.

## 2. Capabilities

Because the V2 baseline targets Windows desktop playback, the app must not ask for unnecessary permissions.

- Required capabilities:
  - `internetClient` for HTTP/HLS/MPEG-TS streaming and Store receipt flows.
  - `runFullTrust` for the packaged Windows App SDK / WinUI 3 entrypoint token used by this app.
- runFullTrust justification:
  - KROIRA is a packaged WinUI 3 desktop application that uses the Windows App SDK full-trust desktop entry point and local native playback components.
  - The capability is required for the packaged desktop app launch model and native media playback integration.
  - It is not used to bypass DRM, paywalls, authentication, or access controls.
- Do not add `privateNetworkClientServer`, broad filesystem access, or `backgroundMediaPlayback` unless that scope is separately productized and validated.

## 3. Validation Gate

Run the baseline validation before packaging:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-v2-release.ps1 -Configuration Debug -Platform x64
```

If packaging is needed locally and unsigned MSIX output is acceptable for review, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-v2-release.ps1 -Unsigned -SkipValidation -Configuration Release -Platform x64
```

Do not upload unsigned packages to Partner Center.

## 4. Store Copy And Content Policy

Recommended short description:

> Bring your own IPTV sources to a local-first Windows media player and source manager.

Recommended long description:

> KROIRA IPTV is a Windows media player and source manager for user-provided M3U playlists, Xtream providers, and Stalker portals. It helps organize live channels, VOD libraries, guide data, favorites, continue-watching state, and source diagnostics on this device. KROIRA does not provide channels, streams, playlists, subscriptions, accounts, credentials, or media content.

Required disclaimer language:

> KROIRA is a media player and source manager. KROIRA does not provide channels, streams, playlists, subscriptions, or media content. Users are responsible for adding only authorized sources and for complying with applicable provider terms and laws. KROIRA does not bypass DRM, paywalls, authentication, or access controls.

The listing must not imply that KROIRA supplies channels, playlists, accounts, credentials, subscriptions, provider access, or copyrighted content.

## 5. Screenshots And Assets

- Capture screenshots only from sanitized/sample source data that you own or have permission to show.
- Do not publish screenshots that expose private playlist URLs, credentials, MAC addresses, personal profile names, third-party provider brands, channel logos, copyrighted posters, or debug tooling.
- Use `scripts/store-screenshots.ps1` with `-SanitizedDataConfirmed` only after manually confirming the data set is store-safe.
- Follow `docs/store_screenshot_workflow.md` and `docs/screenshots/README.md`.
- Generated screenshots belong under `artifacts/store-screenshots` and are ignored by git.
- Screenshots must not imply that KROIRA provides channels, streams, playlists, subscriptions, or media content.

## 6. Privacy And Support

- Link the Store listing to the published privacy policy.
- Privacy URL: `https://sativagenetics.github.io/KroiraIPTV/privacy.html`.
- Support URL: `https://sativagenetics.github.io/KroiraIPTV/support.html`.
- The privacy copy must state that KROIRA stores source settings, credentials, guide state, favorites, playback progress, diagnostics, and preferences locally on the device.
- The privacy copy must describe protected credential handling, sanitized logs, no bundled telemetry or advertising analytics, and metadata provider usage.
- The support copy must cover source authentication failure, no channels, no EPG, wrong EPG, stream playback failure, Store/MSIX install, app data reset, and diagnostics export.
- Confirm the support URL and support email are current before submission.

## 7. Monetization Metadata

- Configure the application listing in Partner Center as `Free` unless the release plan explicitly changes.
- Do not publish paid add-ons or in-app purchase SKUs unless entitlement behavior and Store receipt validation have been separately validated for the release.
