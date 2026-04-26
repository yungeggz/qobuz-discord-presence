# Qobuz Discord Presence

A lightweight Windows tray app that shows your currently playing Qobuz track as Discord Rich Presence.

It displays the current track, artist, album artwork, synced track countdown, and optional audio quality such as:

```text
Lossless • 16-bit / 44.1 kHz • Stereo
Hi-Res • 24-bit / 96 kHz • Stereo
```

## Features

- Discord Rich Presence for Qobuz Desktop
- Track title and artist display
- Album artwork support
- Synced track countdown using Qobuz playback position
- Audio quality display from Qobuz local cache
- Start with Windows option
- Tray-first Windows app
- Clears Discord presence when Qobuz is paused, idle, or closed
- No Qobuz login required

## Requirements

- Windows
- Qobuz Desktop
- Discord Desktop
- .NET 10 Desktop Runtime, unless using a self-contained build

## How it works

Qobuz Discord Presence reads local Qobuz Desktop state from:

```text
%APPDATA%\Qobuz\player-*.json
%APPDATA%\Qobuz\qobuz.db
```

It uses the player JSON to determine the current queue item, playback position, and timestamp. It uses the local SQLite cache to resolve track metadata and audio quality.

The app sends track title, artist, album artwork, audio quality, and countdown timing to Discord Rich Presence.

## Privacy

This app reads local Qobuz Desktop runtime/cache files on your machine.

It does not ask for, read, store, or transmit your Qobuz password.

It sends the following current-listening information to Discord Rich Presence:

- Track title
- Artist
- Album artwork URL when available
- Audio quality when available
- Track timing

## Usage

1. Start Qobuz Desktop.
2. Start Discord Desktop.
3. Run Qobuz Discord Presence.
4. Play music in Qobuz.
5. Your Discord profile should show the current Qobuz track.

The app runs from the Windows system tray.

## Settings

Open the tray menu and choose **Open Settings**.

Available settings:

- Show audio quality in visible status line
- Show audio quality in album-art hover text
- Start with Windows

## Start with Windows

When enabled, the app registers itself to start when you sign in to Windows.

If you move the app to a different folder after enabling this setting, disable and re-enable **Start with Windows** so the startup path is refreshed.

## Building from source

```powershell
dotnet restore .\src\QobuzPresence.App\QobuzPresence.App.csproj
dotnet build .\src\QobuzPresence.App\QobuzPresence.App.csproj
dotnet run --project .\src\QobuzPresence.App\QobuzPresence.App.csproj
```

## Publishing

Framework-dependent publish:

```powershell
dotnet publish .\src\QobuzPresence.App\QobuzPresence.App.csproj -c Release -r win-x64 --self-contained false -o .\publish
```

Self-contained publish:

```powershell
dotnet publish .\src\QobuzPresence.App\QobuzPresence.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

## Notes

This is an unofficial community project and is not affiliated with Qobuz or Discord.

Qobuz Desktop local file formats may change in future versions, which could require app updates.

## License

MIT