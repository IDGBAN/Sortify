<img src="Sortify/Assets/sortify-logo.svg" alt="Sortify logo" width="96">

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
- A **Compare** tab that puts two periods side by side (it starts with your last 12 months against the 12 before): listening time, plays, artists and the rest with how much each changed, your artists and tracks ranked in both with how far they climbed or fell, and the artists that are new in the second period or gone from it. One click swaps the periods or makes the first one the stretch just before the second. Every sidebar filter except the date range applies to both.
  Switch it to **You and someone else** and open another person's export (their ZIP, folder or JSON files) to see your listening next to theirs: the same headline figures and rankings, a taste overlap score (how much of your listening time and theirs goes to the same artists), the artists you have most in common, and the ones only one of you plays. Every sidebar filter applies to both of you, dates included. Their history is held in memory until you close it or Sortify, and is never cached or saved.
- A **Years** tab with a per-year rollup: listening time, plays, unique artists and tracks, and your top artist and track for every year. Switch its chart to **By year found** to split each year by when you first heard the music: that year, the year before, two or three years before, or earlier, with a line saying how much of your latest year was new. Below the table, **Rank History** follows your top eight artists from year to year: where each one ranked by listening time, with a gap for any year they fell out of the top 20. Click a point to open that artist.
- **Year in review image**: right-click a year on the Years tab (or use the button in a year's detail view) for a 1080 × 1350 card with your minutes, plays, artists, new artists, longest streak, top five artists and tracks, and biggest month and day. Save it as a PNG or copy it. It follows the light or dark theme, and says so on the card when other filters are on.
- **Summary card**: the same kind of image for any stretch of time. The button at the top of the Overview tab, or **Export > Summary card image**, makes one for the dates set in the sidebar (a month, a summer, the last 30 days) or for your whole history when none are set. The heading names the range, and a range shorter than two months names its biggest week instead of its biggest month.
- **Listening calendar**, on the Trends tab: a year of days, one square each, shaded by how long you listened (the shades split your listening days into quarters, so a light listener's calendar reads as well as a heavy one's). Buttons switch the year, hovering a day shows its listening, and clicking it opens that day's breakdown: when you started and stopped, the hours you listened, the artists, tracks and albums, and every play. The biggest day on the Insights tab opens the same way.
- **Compare artists**, on the Trends tab: up to five artists' listening month by month (or week by week) on one chart. It starts with your top three; type a name to add another.
- **Your #1 each month**, on the Trends tab: every month's top artist and top track, and which artist topped the most months.
- A **Podcasts** tab covering podcasts and audiobooks: total time and plays, how many shows and episodes, top shows and top episodes, and sortable tables for both. Double-click a show or episode (or an audiobook or chapter) for its own detail view, whether or not podcasts count toward your music stats.
- Insights: longest and current listening streaks, your longest break, your biggest day, listening sessions (count, average and longest), weekday vs. weekend split, favorite time of day, skip rate, completion rate, repeat rate, discovery rate, how much of your time goes to a single artist, most skipped tracks, a chart of new artists discovered per month, and a donut of why plays ended (finished, skipped, and so on).
- **Milestones**, also in Insights: your first play, the plays that reached 1,000, 10,000, 50,000 (and so on) plays and 100, 500, 1,000 (and so on) hours, and the first time you heard your top artist.
- **On repeat**, also in Insights: the tracks you played at least 10 times within seven days, each at its busiest week, with the dates. Copy them as links to relive the stretch.
- **Forgotten favorites**, also in Insights: tracks you played at least 20 times and artists you played at least 50 times that have gone quiet for the last six months of your history. Copy the tracks as links to hear them again.
- Playback context, also in Insights: your shuffle rate, how much you listened offline or in private sessions, and donuts breaking listening down by device (desktop, mobile, web player, speaker, car) and by country.
- **Light and dark themes**, switchable from the toolbar (Ctrl+T) or set to follow Windows (including when you switch Windows' mode while Sortify is open).
- Sortable tables. Click any column header to reorder by that field; right-click a row to copy it or exclude that track/artist. Besides time and plays, the tables show each track's skip rate, how many of an artist's tracks you played and in how many months, and how many tracks of each album you heard. Ctrl- or Shift-click to pick several rows and copy or exclude them all at once. Each table has its own filter box that narrows the rows instantly, without re-running the analysis.
- **Make a playlist from your stats**: on the Tracks tab, **Copy as links** puts the top 25, 50 or 100 rows (in whatever order the table is sorted) or the rows you picked on the clipboard as Spotify links. Paste them into a playlist in the Spotify desktop app. Set the date filter to a year first to get that year's most played songs. Only the extended streaming history records track links.
- A **History** tab listing every play that passes the filters, newest first: when it ended, the track, artist and album, how long it played, why it ended, the device and country, and whether it was shuffled, offline or private. Sort by any column, narrow it with its own filter box, or pick a day under **Go to** to jump to it. Double-click a play for its track's breakdown.
- **Double-click any track, artist, album or year row** to open a detail view: listening time, plays, active days, first and last listen, where it ranks and its share of your listening, its skip rate, busiest month, longest run of days and the device you played it on most, a monthly chart, an hour-of-day profile, its tracks and albums, every play of it, and a button that opens it in Spotify. Double-click a track, album or artist inside a detail view, or click the artist's name under a track or album, to open that one in turn.
- **Right-click any chart** to copy it to the clipboard or save it as a PNG, or choose **Show the numbers** to see the figures behind it as a table you can sort and copy as CSV. Every chart can also be reached with Tab: Enter opens its numbers and Shift+F10 its menu, so a screen reader gets at the data too.
- **Click a bar** in the track, artist, album, show, episode or most-skipped charts, a year on the Years chart, or a point on the rank history, to open its detail view. Click a square on the heatmap to narrow everything to that hour of that day.
- Filters that update every chart and table as you change them, with a chip row showing which ones are active. The x on a chip clears just that filter:
  - Minimum play duration. The default is five seconds, which drops skips.
  - Whether podcasts and audiobooks count toward your track, artist and album statistics. Off by default, since a few long shows will otherwise outrank your music. The Podcasts tab shows them either way.
  - **Combine remasters and deluxe editions**: counts "Song - 2011 Remaster" as "Song" and "Album (Deluxe Edition)" as "Album", so a favourite that Spotify moved to a re-release isn't split across rows. It folds remasters (with or without a year), mono, stereo, single and album versions, and deluxe, expanded, anniversary and bonus-track editions; live versions, remixes, radio edits and featured artists stay as they are. Off by default. An exclusion made while it's on names the combined title, so it won't match the separate editions once it's off again.
  - Date range, typed in or picked with one click: all time, the last 30 days or 12 months of your history, or any single year. Right-click a year on the Years tab to filter everything to it.
  - Search by track, artist or album name.
  - Only these artists: narrow every tab to one artist or a handful. Type a name and pick a match, right-click rows in the Tracks, Artists or Albums table and choose **Show only this artist**, or use the button in an artist's detail view.
  - An exclude list for specific artists or tracks.
  - Time of day and day of week. Time ranges may cross midnight, for example 22:00 to 02:00.
  - Playback: count only shuffled, offline or private-session plays, or leave them out, and untick any device or country you want left out. Older exports don't record any of this, so their plays always count unless one of the lists is set to "Only".
- **Undo and redo for filters**: the arrows at the start of the filter row, Ctrl+Z and Ctrl+Y, or a mouse's back and forward buttons step through your last 50 filter changes, so a heatmap click, a quick range or **Clear all** can be taken back.
- **Saved filters**: name the current filters at the top of the sidebar and pick them from the list later to put them all back. Exclusions (unticked devices and countries included), the minimum play duration, and the podcast, re-release and private-session settings are kept between launches on their own; date ranges, searches, the artists picked to show on their own, time windows and the shuffle and offline settings reset each time you open Sortify.
- Load data your way: open the ZIP Spotify sends without unzipping it, pick individual JSON files (both with Ctrl+O), point at the extracted export folder (**Open Folder**, Ctrl+Shift+O), reopen one of the last eight exports from **Recent**, or just drag & drop the ZIP, the files or the whole folder onto the window.
- **Sample history**: no export yet? The start screen opens two years of made-up listening (invented artists, tracks, podcasts and an audiobook) so you can try every tab while Spotify prepares yours. It isn't saved or added to Recent, and opening your own export replaces it.
- Reads both the extended streaming history (`Streaming_History_Audio_*.json`) and the older account-data export (`StreamingHistory*.json`, its podcast file included). Load both together and each play is still counted once: account-data plays the extended history already has are left out, and the newer ones are kept, so a recent account-data export can top up an older extended one. Loading the same export twice doesn't double anything either.
- Reopens the folder you used last time when it starts, remembers your window size and layout, and caches the parsed history so an unchanged export loads instantly instead of being re-read.
- **What was loaded**: the link at the right of the status bar (such as "12 files loaded") lists every file that was read, whether it's the extended history or the older account-data export, how many plays it held and the dates they cover, and why any file was skipped. It also says how many plays more than one file held, and, when only the account-data export is open, what that export leaves out.
- Export a text summary, a Markdown report, everything as JSON, or tracks, artists, albums, years and shows as CSV. The text, Markdown and JSON exports say which filters they were made under. **Every play as CSV** writes the plays that pass the filters one per row, cleaned up, with duplicates dropped and times in your local time, for your own analysis in a spreadsheet or pandas.
- Settings for the theme, chart animations, the day weeks start on (Sunday, Monday or Saturday, which orders the heatmap, the day-of-week chart and the sidebar, and decides where weekly totals begin), how long a break has to be before it starts a new listening session, and clearing the cache.
- **Time zones that follow you**: Spotify records every play in UTC, and Sortify shows plays in your PC's time zone, which gets the hours wrong for anything you played before a move or on a trip. Turn on **Settings → Time Zone → Show each play in the time zone it was streamed from** and each play keeps the clock of the country Spotify says it came from. Countries with several time zones (the US, Canada, Mexico, Brazil, Russia, Australia, Indonesia and a few others) and plays with no country, such as everything in the older account-data export, use a zone you pick. A few countries have a small region on another clock and use the main one: Spain's Canary Islands and Portugal's Azores show an hour off. A VPN shows up as the VPN's country. It's off by default, and changing it reads your export again.

## Keyboard Shortcuts
| Shortcut | Action |
| --- | --- |
| `Ctrl+O` / `Ctrl+Shift+O` | Open the export ZIP or files / open a folder |
| `F5` | Re-read the files from disk, ignoring the cache |
| `Ctrl+1` … `Ctrl+9`, `Ctrl+0` | Jump to a tab (`Ctrl+0` is History) |
| `Ctrl+K` | Jump to any artist, track, album or podcast by name and open its detail view |
| `Ctrl+F` | Focus the current table's filter box (`Esc` clears it) |
| `Enter` | Open the detail view for the selected track, artist, album or year row |
| `Ctrl+E` / `Ctrl+M` / `Ctrl+J` | Export text / Markdown / JSON |
| `Ctrl+B` | Show or hide the filter sidebar |
| `Ctrl+T` | Switch between the light and dark themes |
| `Ctrl+R` | Reset every filter |
| `Ctrl+Z` / `Alt+Left` | Undo the last filter change (outside a text box that has its own typing to undo) |
| `Ctrl+Y` / `Alt+Right` | Redo the filter change you undid |

## Get Started
1. Request your [extended streaming history](https://www.spotify.com/ca-en/account/privacy/) from Spotify. When it arrives, download the ZIP. There's no need to extract it.
2. Download the latest `Sortify.exe` from the [releases page](https://github.com/IDGBAN/Sortify/releases/) and run it.
3. Drag the ZIP onto the window, or click **Open Files** and pick it. An extracted folder works too: use **Open Folder**, or drop the folder or its JSON files onto the window.
4. Browse the Overview, Tracks, Artists, Albums, Years, Podcasts, Trends, Insights, Compare and History tabs, adjust the filters on the left, and export your results if you want a copy.

While you wait for the export, **Explore a sample history while you wait** on the start screen shows what Sortify does with two years of made-up listening.

Analysis is quick unless your history is unusually large.

## Building from Source
You need the [.NET 8 SDK](https://dotnet.microsoft.com/download). With a newer SDK installed alongside it, `global.json` still has `dotnet` use the 8.0 one, as CI does.

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
- Everything runs locally on your machine. The only files it writes outside your exports are a settings file, a cache of your parsed history and an error log (plus the previous log once it passes 1 MB, and any settings file it couldn't read), all in `%LOCALAPPDATA%\Sortify`; deleting that folder resets everything. **Settings → Open data folder** takes you there.
- Please **review the code** before you download and run it.

## License
Released under the [GNU AGPLv3](LICENSE) license.
