# Music Honorific

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for Final Fantasy XIV that syncs your currently playing track (Deezer, Spotify, browser, etc.) to your in-game [Honorific](https://github.com/goatcorp/DalamudPlugins) title.

---

## ⚠️ Prerequisites

### 1. Honorific (required)
Install **Honorific** by Caraxi from the standard Dalamud plugin repository.  
This plugin uses Honorific's IPC to set your character title.

### 2. Windows Media Session support (required)
This plugin reads track metadata and playback state through the Windows media API:
`GlobalSystemMediaTransportControlsSessionManager`.

No external RPC helper is required.

---

## Installation

1. Add the plugin's custom repository to Dalamud (or load it as a dev plugin)
```
https://raw.githubusercontent.com/kyaEH/MusicHonorific/master/repo.json
```
2. Install **Music Honorific** from the plugin installer
3. Open the plugin window with `/musichonorific` (or via the plugin list)

---

## Usage

1. Open the plugin window
2. Make sure a media source exposing a Windows media session is running (Deezer, Spotify, browser, etc.)
3. Check **"Sync song to Honorific title"** to enable syncing
4. Your Honorific title will update to reflect the current track:
   - Format: `♪ Artist... - Song ♪`
   - Glow color: purple `#A238FF`
   - Every 60 seconds, shows a source message for 10 seconds
     (`♪ Listening on Deezer ♪`, `♪ Listening on Spotify ♪`, or `♪ Listening to music ♪`)
   - Automatically clears when playback is paused/stopped

Use the **Refresh** button to force an immediate update.

---

## Title Format

| Situation | Display |
|---|---|
| Artist + Song fit | `♪ Falling In Reverse - Bad Guy ♪` |
| Artist too long | `♪ Falling In Rev... - Bad Guy ♪` |
| No artist | `♪ Bad Guy ♪` |
| Every 60s for 10s | `♪ Listening on Deezer ♪` / `♪ Listening on Spotify ♪` / `♪ Listening to music ♪` |

Maximum title length is **32 characters** (Honorific limit).

---

## Credits

- **Author** — Kyaeh / Pastalix / Lianh Procyon
- **Inspired by** — [SpotifyHonorific](https://github.com/Valiice/SpotifyHonorific) by Valiice and [PatMeHonorific](https://github.com/anya-hichu/PatMeHonorific) by anya-hichu
- **Built from** — [SamplePlugin](https://github.com/goatcorp/SamplePlugin) by goatcorp
- **Media API** — Windows GlobalSystemMediaTransportControlsSessionManager

---

## License

AGPL-3.0-or-later
