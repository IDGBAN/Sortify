# Sortify

[![CI](https://img.shields.io/github/actions/workflow/status/IDGBAN/Sortify/ci.yml?branch=main&label=CI&logo=github)](https://github.com/IDGBAN/Sortify/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/IDGBAN/Sortify?label=release&color=1DB954&logo=github)](https://github.com/IDGBAN/Sortify/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/IDGBAN/Sortify/total?color=1DB954&logo=github)](https://github.com/IDGBAN/Sortify/releases)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download)
![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows&logoColor=white)
[![License: AGPL v3](https://img.shields.io/github/license/IDGBAN/Sortify?color=663366)](LICENSE)

Sortify reads your Spotify streaming history and turns it into statistics you can explore. It shows your top tracks, artists and albums, how your listening changed over time and year by year, and how it breaks down by hour of the day and day of the week. It ships as a single portable `.exe` so there is no installation.

## Features
- Top tracks, artists and albums, ranked by listening time, play count, or the first time you played them.
- Charts throughout the app: bar charts for top tracks, artists and albums, a donut showing each artist's share of your listening, a line chart of listening over time (daily, weekly or monthly), a day-of-week vs. hour-of-day heatmap, and breakdowns by hour and by day of the week.
- A **Years** tab with a per-year rollup: listening time, plays, unique artists and tracks, and your top artist and track for every year.
- A **Podcasts** tab covering podcasts and audiobooks: total time and plays, how many shows and episodes, top shows and top episodes, and sortable tables for both.
- Insights: longest and current listening streaks, your biggest day, listening sessions (count, average and longest), weekday vs. weekend split, favorite time of day, skip rate, most skipped tracks, a chart of new artists discovered per month, and a donut of why plays ended (finished, skipped, and so on).
- Playback context, also in Insights: your shuffle rate, how much you listened offline, and donuts breaking listening down by device (desktop, mobile, web player, speaker, car) and by country.
- Sortable tables. Click any column header to reorder by that field; right-click a row to copy it or exclude that track/artist.
- **Double-click any track, artist or album row** to open a detail view: listening time, plays, active days, first and last listen, a monthly chart, an hour-of-day profile, its tracks and albums, and a button that opens it in Spotify.
- Filters that update every chart and table as you change them:
  - Minimum play duration. The default is five seconds, which drops skips.
  - Whether podcasts and audiobooks count toward your track, artist and album statistics. Off by default, since a few long shows will otherwise outrank your music. The Podcasts tab shows them either way.
  - Date range.
  - Search by track, artist or album name.
  - An exclude list for specific artists or tracks.
  - Time of day and day of week. Time ranges may cross midnight, for example 22:00 to 02:00.
- Load data your way: pick individual JSON files (Ctrl+O), point at the extracted export folder (**Open Folder**, Ctrl+Shift+O), or just drag & drop the files or the whole folder onto the window.
- Reads both the extended streaming history (`Streaming_History_Audio_*.json`) and the older account-data export (`StreamingHistory*.json`).
- Reopens the folder you used last time when it starts, and caches the parsed history so an unchanged export loads instantly instead of being re-read.
- Export to TXT (with a summary section), or export tracks, artists, albums and years to CSV.

## Get Started
1. Request your [extended streaming history](https://www.spotify.com/ca-en/account/privacy/) from Spotify. When it arrives, download and extract the ZIP.
2. Download the latest `Sortify.exe` from the [releases page](https://github.com/IDGBAN/Sortify/releases/) and run it.
3. Click **Open Folder** and pick the extracted folder (or click **Run Analysis** to choose individual JSON files, or drag & drop them onto the window).
4. Browse the Overview, Tracks, Artists, Albums, Years, Podcasts, Trends and Insights tabs, adjust the filters on the left, and export your results if you want a copy.

Analysis is quick unless your history is unusually large.

## Building from Source
You need the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project Sortify/Sortify.csproj

dotnet publish Sortify/Sortify.csproj -c Release -r win-x64
```

The published `Sortify.exe` lands in `Sortify/bin/Release/net8.0-windows/win-x64/publish/`.

To run the test suite:

```bash
dotnet test
```

## Disclaimer
- Sortify is not affiliated with Spotify.
- Everything runs locally on your machine. The only files it writes outside your exports are a settings file and a cache of your parsed history, both in `%LOCALAPPDATA%\Sortify`; deleting that folder resets both.
- Please **review the code** before you download and run it.

## License
Released under the [GNU AGPLv3](LICENSE) license.
