# Sortify

## About
Sortify reads your Spotify extended streaming history and turns it into statistics you can explore. It shows your top tracks and artists, how your listening changed over time, and how it breaks down by hour of the day and day of the week. It ships as a single portable `.exe` so there is no installation.

## Features
- Top tracks and artists, ranked by listening time, play count, or the first time you played them.
- Charts throughout the app: bar charts for top tracks and artists, a donut showing each artist's share of your listening, a line chart of listening over time, and breakdowns by hour and by day of the week.
- Sortable tables. Click any column header to reorder by that field.
- Filters that update every chart and table as you change them:
  - Minimum play duration. The default is five seconds, which drops skips.
  - Date range.
  - Search by track or artist name.
  - An exclude list for specific artists or tracks.
  - Time of day and day of week. Time ranges may cross midnight, for example 22:00 to 02:00.
- Export to TXT, or export tracks and artists to CSV.

## Get Started
1. Request your [extended streaming history](https://www.spotify.com/ca-en/account/privacy/) from Spotify. When it arrives, download and extract the ZIP.
2. Download the latest `Sortify.exe` from the [releases page](https://github.com/IDGBAN/Sortify/releases/) and run it.
3. Click **Run Analysis** and choose the JSON files you want to include.
4. Browse the Overview, Tracks, Artists, and Trends tabs, adjust the filters on the left, and export your results if you want a copy.

Analysis is quick unless your history is unusually large.

## Building from Source
You need the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project Sortify/Sortify.csproj

dotnet publish Sortify/Sortify.csproj -c Release -r win-x64
```

The published `Sortify.exe` lands in `Sortify/bin/Release/net8.0-windows/win-x64/publish/`.

## Disclaimer
- Sortify is not affiliated with Spotify.
- Everything runs locally on your machine.
- Please **review the code** before you download and run it.
