# KROIRA Package Assets

These files are the package-facing visual assets for the V2 release-candidate app. The current purple/navy K streaming mark remains the official brand mark; do not replace it with a new brand direction unless the product identity is intentionally changed.

## Manifest Assets

`Package.appxmanifest` references the base filenames below. Windows can then select matching scale, target-size, and theme variants from `src/Kroira.App/Assets/`.

| Asset | Used for | Notes |
| --- | --- | --- |
| `StoreLogo.png` | Package/Store logo resource | Transparent square-safe K mark. Scale variants are provided for common Store package sizes. |
| `Square44x44Logo.png` | App icon base resource | Transparent square-safe K mark. `targetsize-*` variants cover taskbar, Start, app list, search, and shell icon sizes. |
| `Square44x44Logo.targetsize-*_altform-unplated.png` | Dark shell unplated app icon variants | Same K mark with a subtle contrast halo so the navy edge remains legible on dark shell surfaces. |
| `Square44x44Logo.targetsize-*_altform-lightunplated.png` | Light shell unplated app icon variants | Transparent K mark without the dark-surface halo. |
| `Square150x150Logo.png` | Medium tile logo | Transparent square-safe K mark. Scale variants are provided. |
| `Wide310x150Logo.png` | Wide tile logo | Existing dark/cinematic KROIRA tile artwork. Scale variants are provided. |
| `SplashScreen.png` | App splash screen | Existing dark/cinematic KROIRA splash artwork. Scale variants are provided. |
| `Home/match-center-hero.png` | In-app Home visual | Product UI artwork, not a package icon. |

Keep the manifest pointed at the base names. Do not reference qualified variants directly from the manifest.

## Store And Packaging Notes

- The square icon set is intentionally simplified to the K mark only, because the wide/splash artwork is not readable at small Windows shell sizes.
- Transparent variants are preferred for app icon, Store logo, and medium tile surfaces.
- Dark/cinematic background variants are kept for wide tile and splash contexts where the larger composition is readable.
- The current repository does not include a vector or high-resolution transparent brand master. The high-DPI outputs were generated from the existing bitmap assets. If a vector/source-art file becomes available before final Store submission, re-export `Square44x44Logo.targetsize-256.png`, `Square150x150Logo.scale-400.png`, `Wide310x150Logo.scale-400.png`, and `SplashScreen.scale-400.png` from that source.

Reference guidance:

- Windows app icon construction: https://learn.microsoft.com/windows/apps/design/iconography/app-icon-construction
- Windows resource qualifiers: https://learn.microsoft.com/windows/apps/windows-app-sdk/mrtcore/images-tailored-for-scale-theme-contrast
