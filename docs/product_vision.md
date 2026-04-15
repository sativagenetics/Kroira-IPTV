# Product Vision: KROIRA IPTV

## 1. Core Identity
The app is a premium, high-performance BYOC (Bring Your Own Content) IPTV player built natively for Windows Desktop via WinUI 3 and .NET 8. It provides a world-class user experience, focusing heavily on performance, rendering fidelity, and keyboard/mouse/remote usability.

## 2. Legal Product Positioning (Player-Only App)
KROIRA IPTV is strictly a media playback utility. 
- It acts solely as an interface for content the user already possesses and has the legal right to stream or view.
- Under no circumstances does this application provide, host, bundle, curate, or recommend media content, playlists, or stream links.
- The UI, onboarding, and store listing must use purely generic industry terms (Live TV, VOD, Channels).

## 3. Privacy & Data Handling Stance
- **Device-Local Core**: All user-provided credentials, playlists, watch histories, and favorites are stored exclusively on the user's local Windows device within an SQLite database.
- **No Telemetry on Content**: The application absolutely does NOT track, phone-home, or log the names of channels watched or URLs consumed. 
- **Analytics**: Basic, anonymized crash reports are permitted (if opted-in by the user) but cannot bundle database dumps.

## 4. In-Scope vs Out-Of-Scope Boundaries
**In-Scope:**
- Parsing remote/local M3U/M3U8 URLs.
- Authenticating with standard Xtream Codes API endpoints.
- Playing diverse video codecs natively encoded in streams (via LibVLC).
- Syncing XMLTV EPG data.
- Managing Favorites and local personalization.

**Out-Of-Scope (V1 & Beyond):**
- Cloud syncing of user configurations or playlists.
- Acting as an IPTV reselling mechanism.
- DRM circumvention layers.
- In-built social features.

## 5. Free Tier Usability Principle
The basic Free tier must be a genuinely amazing, unlimited playback experience.
- The Free tier is NOT a time-limited trial.
- Core playback (starting a stream, opening fullscreen, seeing basic EPG) must **never** be paywalled.
- The Pro tier exists to monetize power-users (those who manage multiple sources, need PIP, or want advanced local PVR/recording functionality). The free tier must never feel purposely broken or nag-heavy.
