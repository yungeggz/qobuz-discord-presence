# Repo Notes

## Overview

- `src/QobuzPresence.App` is the Windows tray app that reads local Qobuz state and updates Discord Rich Presence.
- `src/QobuzPresence.Shared` contains shared helpers/models used by both the app and probe.
- `tools/QobuzCacheProbe` is a local console probe for inspecting `qobuz.db` and simulating resolver/title-match behavior.
- `scripts/Release.ps1` builds release artifacts.

## Qobuz Data Sources

- Player state: `%APPDATA%\Qobuz\player-*.json`
- Database/cache: `%APPDATA%\Qobuz\qobuz.db`

## Presence Resolution

- `QobuzStateReader` reads the selected queue `TrackId` and playback timing from `player-*.json`.
- `QobuzWindowReader` and `QobuzWindowTitleParser` read the visible Qobuz window title and split it into title/artist.
- `QobuzTrackResolutionHelper` decides whether the selected `TrackId` metadata can be used.
- Shared title/artist comparison lives in `QobuzPresence.Shared/Helpers/TrackMatchingUtility.cs`.
- Shared JSON/metadata parsing lives in `QobuzPresence.Shared/Helpers/JsonElementHelper.cs` and `QobuzPresence.Shared/Helpers/QobuzTrackMetadataParser.cs`.
- Current title match stages are:
  - `Exact`
  - `StrippedDecoration`
  - `Substring`
  - `None`
- If the selected track matches the window title/artist, the app keeps DB metadata such as cover art, quality, and duration.
- If the window title is a more specific variant, such as a remix/version suffix, the resolver may keep DB metadata but prefer the window title text.

## Diagnostics

- Tray menu action: **Write Diagnostics Snapshot**
- Main implementation: `src/QobuzPresence.App/Services/QobuzDiagnosticService.cs`
- The snapshot includes:
  - visible Qobuz windows and parsed title/artist
  - selected queue item from `player-*.json`
  - `L_Track`, `S_Track`, and `S_Track_fts` lookup by selected `TrackId`
  - `ArtistMatches` and `TitleMatchStage` for window vs DB
  - resolver preview with resolved title, artist, cover URL, quality, and duration

## Probe Tool

- Project: `tools/QobuzCacheProbe`
- Useful commands:
  - `dotnet run --project tools/QobuzCacheProbe -- "Stuck" "Monsta X"`
  - `dotnet run --project tools/QobuzCacheProbe -- --match-titles "Stuck" "Stuck (Japanese Version)"`
  - `dotnet run --project tools/QobuzCacheProbe -- --track-id 49025273 --window-title "Stuck (Japanese Version)" --window-artist "Monsta X"`
- The probe uses shared helpers from `QobuzPresence.Shared` so its matching/parsing behavior stays aligned with the app.

## Known Environment Constraint

- In sandboxed Codex sessions, `dotnet restore` / `dotnet build` may fail because NuGet signature checks require network access.
- Existing local outputs and `qobuz.db` reads are still usable for investigation.
