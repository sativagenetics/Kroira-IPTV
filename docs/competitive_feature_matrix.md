# Competitive Feature Matrix

KROIRA IPTV is built to exceed the current top-tier Windows Store players.

## 1. Source Onboarding
| Feature | Basic Competitors | KROIRA Target |
|---------|-------------------|---------------|
| Import Types | M3U file upload. | XTREAM API login, M3U URL, Local File. |
| Speed | Freezes UI on large files. | Async background parsing, chunked DB inserts. |
| Diagnostics | Basic "Failed" message. | Deep diagnostics: HTTP code tracing, DNS resolution logging for bad URLs. |

## 2. Playback
| Feature | Basic Competitors | KROIRA Target |
|---------|-------------------|---------------|
| Buffering | Static default. | Configurable Network Caching / timeout limits. |
| External Player | None, forced internal. | Handoff contract to VLC/MPC-HC if codec fails. |
| Audio/Subtitles | Fixed to default track. | Dynamic real-time subtitle & audio track switching from stream metadata. |

## 3. TV Features
| Feature | Basic Competitors | KROIRA Target |
|---------|-------------------|---------------|
| EPG | Slow, limited memory. | Local SQLite EPG sync, instant grid scrolling. |
| Catchup | None. | Seamless timeline scrollback unlocking archive links. |
| Watch State | Nothing. | "Continue Watching" VOD state + global favorites. |

## 4. Power Features
| Feature | Basic Competitors | KROIRA Target |
|---------|-------------------|---------------|
| Downloads | NA | Scheduled local recordings for Live TV & Background VOD downloads. |
| Parental | NA | Parental PIN protection locking specific categories or the entire Source profile. |
| Profiles | Single M3U only. | Multi-source blending and distinct profile sandboxing. |

## 5. Windows-Native UX
| Feature | Basic Competitors | KROIRA Target |
|---------|-------------------|---------------|
| Navigation | Pointer only. | Full Keyboard (Arrow Keys) & Remote (D-Pad) compatibility. Focus rules strictly enforced. |
| Fullscreen | Buggy borderless window. | True OS-level Fullscreen. ESC & F11 perfectly trapped and handled. |
| Multi-monitor | Often crashes LibVLC. | Event listeners handling DPI changes and safely migrating playback buffers. |

## 6. Reliability
| Feature | Basic Competitors | KROIRA Target |
|---------|-------------------|---------------|
| State handling | Crashing when stream drops. | Formal state machine for buffering, reconnect looping, timeout, and safe exit. |

## 7. Legal/Commercial Positioning
| Feature | Basic Competitors | KROIRA Target |
|---------|-------------------|---------------|
| Store Presence | Shady screenshots, fake logos. | Professionally sanitized store assets. Clear player-only copy adhering to MS Store policy. |
