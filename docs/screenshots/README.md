# Repository Screenshots

Store curated repository and release screenshots here only when they are ready for public presentation.

## Rules

- Use a sanitized/sample data set only.
- Do not commit temporary debug captures or ad hoc verification artifacts.
- Do not show provider credentials, playlist URLs, MAC addresses, personal profile names, private source names, unlicensed provider branding, channel logos, or copyrighted artwork.
- Prefer stable PNG captures with product-grade framing and consistent aspect ratios.
- Keep source files small enough for normal repository review.

## Store Capture Workflow

Generated store-review captures belong under `artifacts/store-screenshots` and are ignored by git.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\store-screenshots.ps1 -Configuration Release -Platform x64 -SanitizedDataConfirmed
```

Inspect every generated image before copying approved screenshots into this folder.

## Content Disclaimer

KROIRA IPTV does not include, sell, provide, host, curate, or distribute channels, playlists, stream credentials, subscriptions, or copyrighted content. Screenshots must not imply otherwise.

Update the root `README.md` with relative image links when approved screenshots are added.
