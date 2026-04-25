# KROIRA IPTV Store Submission Info

## App Identity

- App name: KROIRA IPTV
- Public release version: `2.0.0`
- Package manifest identity version: `2.0.0.4`
- App assembly/file version: `2.0.0.0`
- Privacy URL: https://sativagenetics.github.io/KroiraIPTV/privacy.html
- Support URL: https://sativagenetics.github.io/KroiraIPTV/support.html
- Support email: batuhandemirbilek7@gmail.com

## Short Description

Bring your own IPTV sources to a local-first Windows media player and source manager.

## Long Description

KROIRA IPTV is a Windows media player and source manager for user-provided M3U playlists, Xtream providers, and supported provider portal profiles. It helps organize live channels, VOD libraries, guide data, favorites, continue-watching state, playback, and source diagnostics locally on this device.

KROIRA starts empty until the user adds their own authorized source. KROIRA does not provide channels, streams, playlists, subscriptions, accounts, credentials, movies, series, or other media content.

## Required Disclaimer

KROIRA is a media player and source manager. KROIRA does not provide channels, streams, playlists, subscriptions, credentials, or media content. Users are responsible for adding only authorized sources and for complying with applicable provider terms and laws. KROIRA does not bypass DRM, paywalls, authentication, provider restrictions, or access controls.

## First-Run Expectation

KROIRA should be described as an empty player/source manager on first launch. The app only displays catalog entries, guide data, logos, metadata, and streams after the user adds a source they are authorized to use.

## runFullTrust Justification

KROIRA is a packaged WinUI 3 desktop application that uses the Windows App SDK full-trust desktop entry point and local native playback components. The `runFullTrust` capability is required for the packaged desktop app launch model and native media playback integration. It is not used to bypass DRM, paywalls, authentication, provider restrictions, or access controls.

## Privacy Notes

- KROIRA stores source settings, guide state, favorites, playback progress, continue-watching data, diagnostics, protected credential records, and preferences locally on the device.
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
- Audio or subtitle tracks are missing or source-dependent
- Store or MSIX install
- Reset app data
- Export diagnostics

## Copy Rules

Store, release, and repository copy must not imply that KROIRA supplies free TV, included channels, premium content access, provider accounts, stream credentials, subscriptions, copyrighted media, or bundled IPTV services.
