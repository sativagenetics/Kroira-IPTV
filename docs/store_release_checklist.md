# Microsoft Store Release Checklist

## 1. V1 Scope Exclusivity Constraints
Because V1 targets strictly **local Windows desktop playback**, the app must not ask for unnecessary permissions.
- **Required Capabilities in Manifest**:
  - `internetClient` (Mandatory for HTTP/HLS/MPEG-TS streaming and Store receipts).
  - `runFullTrust` (Required by the packaged Windows App SDK / WinUI 3 entrypoint token used by this app).
- **Banned/Downgraded Capabilities**:
  - REMOVE `privateNetworkClientServer` (Since DLNA/Multicast casting is NOT in V1 scope, do not trigger local firewall warnings).
  - REMOVE all broad filesystem access. If we need to write recordings to disk, we rely strictly on the `Windows.Storage.Pickers.FolderPicker` to grant scoped token access.
  - Do not add `backgroundMediaPlayback` unless background playback is explicitly productized and validated.

## 2. Store Copy & Identity Rules
- **Explicit Content Policy**: The description must prominently display: "KROIRA IPTV is a media player utility. Please supply your own legally acquired content. No streams, subscriptions, or channels are provided."
- **Asset Scrubbing**: Store screenshots must be captured from a sanitized/sample source set. Do not publish screenshots that expose private playlist URLs, credentials, MAC addresses, personal profile names, or unlicensed provider branding.
- **Naming**: Keep the app name consistent with the package manifest unless Partner Center copy is intentionally changed. Current app display name is `KROIRA IPTV`.
- **Screenshot Workflow**: Use `scripts/store-screenshots.ps1` with a sanitized/sample data set. Follow `docs/store_screenshot_workflow.md`. Generated screenshots belong under `artifacts/store-screenshots` and are ignored by git.

## 3. Privacy Policy
- Provide a static URL linking to a privacy policy stating explicitly: "KROIRA is entirely device-local. Your media URLs and credentials are saved only on your machine and never transmitted to our telemetry servers."

## 4. Monetization Metadata
- Configure the application listing in Partner Center as "Free".
- Create the mapped "Pro Lifetime Upgrade" In-App Purchase SKU.
