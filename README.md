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
- An **Overview** tab that opens with your headline totals and your top five tracks, artists and albums.
- A **Years** tab with a per-year rollup: listening time, plays, unique artists and tracks, and your top artist and track for every year.
- A **Podcasts** tab covering podcasts and audiobooks: total time and plays, how many shows and episodes, top shows and top episodes, and sortable tables for both.
- Insights: longest and current listening streaks, your longest break, your biggest day, listening sessions (count, average and longest), weekday vs. weekend split, favorite time of day, skip rate, completion rate, repeat rate, discovery rate, how much of your time goes to a single artist, most skipped tracks, a chart of new artists discovered per month, and a donut of why plays ended (finished, skipped, and so on).
- Playback context, also in Insights: your shuffle rate, how much you listened offline, and donuts breaking listening down by device (desktop, mobile, web player, speaker, car) and by country.
- **Light and dark themes**, switchable from the toolbar (Ctrl+T) or set to follow Windows (including when you switch Windows' mode while Sortify is open).
- Sortable tables. Click any column header to reorder by that field; right-click a row to copy it or exclude that track/artist. Each table has its own filter box that narrows the rows instantly, without re-running the analysis.
- **Double-click any track, artist, album or year row** to open a detail view: listening time, plays, active days, first and last listen, a monthly chart, an hour-of-day profile, its tracks and albums, and a button that opens it in Spotify.
- **Right-click any chart** to copy it to the clipboard or save it as a PNG.
- Filters that update every chart and table as you change them, with a chip row showing which ones are active:
  - Minimum play duration. The default is five seconds, which drops skips.
  - Whether podcasts and audiobooks count toward your track, artist and album statistics. Off by default, since a few long shows will otherwise outrank your music. The Podcasts tab shows them either way.
  - Date range.
  - Search by track, artist or album name.
  - An exclude list for specific artists or tracks.
  - Time of day and day of week. Time ranges may cross midnight, for example 22:00 to 02:00.
- **Saved filters**: name the current filters at the top of the sidebar and pick them from the list later to put them all back. Exclusions, the minimum play duration and the podcast setting are kept between launches on their own; date ranges, searches and time windows reset each time you open Sortify.
- Load data your way: open the ZIP Spotify sends without unzipping it, pick individual JSON files (both with Ctrl+O), point at the extracted export folder (**Open Folder**, Ctrl+Shift+O), reopen one of the last eight exports from **Recent**, or just drag & drop the ZIP, the files or the whole folder onto the window.
- Reads both the extended streaming history (`Streaming_History_Audio_*.json`) and the older account-data export (`StreamingHistory*.json`). Load both together and each play is still counted once: account-data plays the extended history already has are left out, and the newer ones are kept, so a recent account-data export can top up an older extended one. Loading the same export twice doesn't double anything either.
- Reopens the folder you used last time when it starts, remembers your window size and layout, and caches the parsed history so an unchanged export loads instantly instead of being re-read.
- Export a text summary, a Markdown report, everything as JSON, or tracks, artists, albums, years and shows as CSV.
- Settings for the theme, chart animations, how long a break has to be before it starts a new listening session, and clearing the cache.

## Keyboard Shortcuts
| Shortcut | Action |
| --- | --- |
| `Ctrl+O` / `Ctrl+Shift+O` | Open the export ZIP or files / open a folder |
| `F5` | Re-read the files from disk, ignoring the cache |
| `Ctrl+1` … `Ctrl+8` | Jump to a tab |
| `Ctrl+F` | Focus the current table's filter box (`Esc` clears it) |
| `Enter` | Open the detail view for the selected track, artist, album or year row |
| `Ctrl+E` / `Ctrl+M` / `Ctrl+J` | Export text / Markdown / JSON |
| `Ctrl+B` | Show or hide the filter sidebar |
| `Ctrl+T` | Switch between the light and dark themes |
| `Ctrl+R` | Reset every filter |

## Get Started
1. Request your [extended streaming history](https://www.spotify.com/ca-en/account/privacy/) from Spotify. When it arrives, download the ZIP. There's no need to extract it.
2. Download the latest `Sortify.exe` from the [releases page](https://github.com/IDGBAN/Sortify/releases/) and run it.
3. Drag the ZIP onto the window, or click **Open Files** and pick it. An extracted folder works too: use **Open Folder**, or drop the folder or its JSON files onto the window.
4. Browse the Overview, Tracks, Artists, Albums, Years, Podcasts, Trends and Insights tabs, adjust the filters on the left, and export your results if you want a copy.

Analysis is quick unless your history is unusually large.

## Building from Source
You need the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project Sortify/Sortify.csproj

# 64-bit (recommended for most systems)
dotnet publish Sortify/Sortify.csproj -c Release -r win-x64

# 32-bit (for older or 32-bit-only Windows installs)
dotnet publish Sortify/Sortify.csproj -c Release -r win-x86
```

The published `Sortify.exe` lands in `Sortify/bin/Release/net8.0-windows/<rid>/publish/`, where `<rid>` is `win-x64` or `win-x86` depending on which you built.

To run the test suite, and to check formatting the way CI does:

```bash
dotnet test
dotnet format --verify-no-changes
```

## Disclaimer
- Sortify is not affiliated with Spotify.
- Everything runs locally on your machine. The only files it writes outside your exports are a settings file, a cache of your parsed history and an error log, all in `%LOCALAPPDATA%\Sortify`; deleting that folder resets everything. **Settings → Open data folder** takes you there.
- Please **review the code** before you download and run it.

## License
Released under the [GNU AGPLv3](LICENSE) license.
