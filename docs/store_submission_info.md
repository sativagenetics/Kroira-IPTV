# KROIRA IPTV Store Submission Info

## App Identity

- App name: KROIRA IPTV
- Release version: 2.0.0
- Privacy URL: https://sativagenetics.github.io/KroiraIPTV/privacy.html
- Support URL: https://sativagenetics.github.io/KroiraIPTV/support.html
- Support email: batuhandemirbilek7@gmail.com

## Short Description

Bring your own IPTV sources to a local-first Windows media player and source manager.

## Long Description

KROIRA IPTV is a Windows media player and source manager for user-provided M3U playlists, Xtream providers, and Stalker portals. It helps organize live channels, VOD libraries, guide data, favorites, continue-watching state, and source diagnostics on this device. KROIRA does not provide channels, streams, playlists, subscriptions, accounts, credentials, or media content.

## Required Disclaimer

KROIRA is a media player and source manager. KROIRA does not provide channels, streams, playlists, subscriptions, or media content. Users are responsible for adding only authorized sources and for complying with applicable provider terms and laws. KROIRA does not bypass DRM, paywalls, authentication, or access controls.

## runFullTrust Justification

KROIRA is a packaged WinUI 3 desktop application that uses the Windows App SDK full-trust desktop entry point and local native playback components. The runFullTrust capability is required for the packaged desktop app launch model and native media playback integration. It is not used to bypass DRM, paywalls, authentication, or access controls.

## Privacy Notes

- KROIRA stores source settings, guide state, favorites, playback progress, diagnostics, and preferences locally on the device.
- Source credentials are used only for sources configured by the user.
- Protected credential copies use Windows DPAPI CurrentUser scope when available, with plaintext compatibility retained for existing import and parser flows.
- Sanitized diagnostics and source reports are designed to mask playlist URLs, usernames, passwords, tokens, keys, MAC-like values, and loose secrets before display or export.
- KROIRA does not bundle app telemetry, advertising analytics, or a vendor-operated media URL collection service in this release.
- Metadata enrichment may contact metadata or artwork providers such as TMDb, plus image URLs supplied by user-configured sources. Source passwords and provider tokens are not sent to TMDb.

## Support Topics

- Source authentication failure
- No channels
- No EPG
- Wrong EPG
- Stream does not play
- Store or MSIX install
- Reset app data
- Export diagnostics
