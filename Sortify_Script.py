import json
import os
from collections import Counter, defaultdict
from datetime import datetime, timedelta
from operator import itemgetter
import glob

def analyze_listening_data(json_file_paths):
    """Analyzes listening data from multiple JSON files, aggregating by artist, track, and year."""

    artist_playtimes = Counter()
    artist_counts = Counter()
    track_playtimes_by_year = defaultdict(Counter)
    track_counts_by_year = defaultdict(Counter)
    track_first_playtimes_by_year = defaultdict(lambda: defaultdict(lambda: datetime.max))
    total_tracks_by_year = defaultdict(int)  # Track total tracks per year
    total_ms_played_by_year = defaultdict(int)  # Track total ms played per year

    for json_file_path in json_file_paths:
        try:
            print(f"Processing file: {json_file_path}")
            with open(json_file_path, 'r', encoding='utf-8') as file:
                if os.stat(json_file_path).st_size == 0:
                    print(f"Warning: {json_file_path} is empty.")
                    continue  # Skip empty files

                try:
                    data = json.load(file)
                    for item in data:
                        artist_name = item.get("master_metadata_album_artist_name", "Unknown Artist")
                        track_name = item.get("master_metadata_track_name", "Unknown Track")
                        ms_played = int(item.get("ms_played", 0))
                        timestamp_str = item.get("ts", None)

                        if artist_name:
                            artist_playtimes[artist_name] += ms_played
                            artist_counts[artist_name] += 1
                        if track_name and timestamp_str:
                            playtime = datetime.strptime(timestamp_str, "%Y-%m-%dT%H:%M:%SZ")
                            year = playtime.year  

                            track_playtimes_by_year[year][track_name] += ms_played
                            track_counts_by_year[year][track_name] += 1
                            total_tracks_by_year[year] += 1
                            total_ms_played_by_year[year] += ms_played
                            track_first_playtimes_by_year[year][track_name] = min(track_first_playtimes_by_year[year][track_name], playtime)

                except json.JSONDecodeError as e:
                    print(f"Warning: Skipping invalid JSON content in {json_file_path}: {e}")
                    continue  # Skip to the next file if JSON is invalid

        except FileNotFoundError:
            print(f"Warning: File not found: {json_file_path}")
            continue  # Skip to the next file if not found

    return (artist_playtimes, artist_counts, track_playtimes_by_year,
            track_counts_by_year, track_first_playtimes_by_year, 
            total_tracks_by_year, total_ms_played_by_year)


def format_timedelta(td):
    """Formats a timedelta object into HH:MM:SS."""
    total_hours = td.days * 24 + td.seconds // 3600
    total_minutes = (td.seconds % 3600) // 60
    total_seconds = td.seconds % 60
    return f"{total_hours:02d}:{total_minutes:02d}:{total_seconds:02d}"

def write_results(output_file, results_type, playtimes_by_year, counts_by_year, 
                 total_tracks_by_year, total_ms_played_by_year, first_playtimes_by_year=None):
    """Writes the analysis results to files, one for each year."""

    artist_data = {}  # Track artists for tracks

    if results_type == "Songs":
        for json_file_path in json_file_patterns:
            with open(json_file_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                for d in data:
                    track_name = d.get("master_metadata_track_name", None)
                    artist_name = d.get("master_metadata_album_artist_name", None)
                    if track_name and artist_name:
                        artist_data[track_name] = artist_name

    for year, playtimes in playtimes_by_year.items():
        total_tracks = total_tracks_by_year[year]
        total_ms_played = total_ms_played_by_year[year]
        total_time_played = timedelta(milliseconds=total_ms_played)
        # Filter to top 1000
        ranked_by_time = sorted(playtimes.items(), key=itemgetter(1), reverse=True)[:1000]
        ranked_by_count = counts_by_year[year].most_common(1000)
        first_playtimes = first_playtimes_by_year.get(year, None)  # Safely get first_playtimes 

        output_file_year = f"{output_file}_{year}.txt" 
        os.makedirs(os.path.dirname(os.path.abspath(output_file_year)), exist_ok=True)

        with open(output_file_year, "w", encoding="utf-8") as outfile:
            outfile.write(f"Year: {year}\n\n")
            outfile.write(f"Total Play Count: {total_tracks}\n")
            outfile.write(f"Total Listening Time: {format_timedelta(total_time_played)}\n\n\n") 

            outfile.write(f"{results_type} Ranked by Play Count:\n")
            for rank, (item, count) in enumerate(ranked_by_count, 1):
                if results_type == "Songs":
                    item_name = f"{item} - {artist_data.get(item, 'Unknown Artist')}"
                else:
                    item_name = item
                outfile.write(f"{rank}. {count} times - {item_name}\n")


            outfile.write(f"\n{results_type} Ranked by Listening Time:\n")
            for rank, (item, ms_played) in enumerate(ranked_by_time, 1):
                if results_type == "Songs":
                    item_name = f"{item} - {artist_data.get(item, 'Unknown Artist')}"
                else:
                    item_name = item
                playtime = timedelta(milliseconds=ms_played)
                outfile.write(f"{rank}. {format_timedelta(playtime)} - {item_name}\n")
            
            # Write first play times **only** if it is song data
            if first_playtimes is not None and results_type == "Songs":
                outfile.write(f"\nFirst Play Time of {results_type}:\n")
                for track, playtime in sorted(first_playtimes.items(), key=lambda x: x[1]):
                    track_name = f"{track} - {artist_data.get(track, 'Unknown Artist')}"
                    outfile.write(f"{playtime.strftime('%Y-%m-%d %H:%M')} - {track_name}\n")


if __name__ == "__main__":
    json_file_patterns = glob.glob("*.json")
    output_file_tracks = "Results_Songs"
    output_file_artists = "Results_Artists"

    (artist_playtimes, artist_counts, track_playtimes_by_year,
     track_counts_by_year, track_first_playtimes_by_year, 
     total_tracks_by_year, total_ms_played_by_year) = analyze_listening_data(json_file_patterns)

    write_results(output_file_artists, "Artists", track_playtimes_by_year, track_counts_by_year, 
                  total_tracks_by_year, total_ms_played_by_year)
    write_results(output_file_tracks, "Songs", track_playtimes_by_year, track_counts_by_year, 
                  total_tracks_by_year, total_ms_played_by_year, track_first_playtimes_by_year)


    print(f"Results saved to separate files for each year.")
