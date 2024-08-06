import json
import os
from collections import Counter
from datetime import timedelta
from operator import itemgetter
import glob

def analyze_listening_data(json_file_paths):
    """Analyzes listening data from multiple JSON files, aggregating by artist and track."""

    artist_playtimes = Counter()
    artist_counts = Counter()
    track_playtimes = Counter()
    track_counts = Counter()
    total_tracks = 0
    total_ms_played = 0

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
                        if artist_name:
                            artist_playtimes[artist_name] += ms_played
                            artist_counts[artist_name] += 1
                        if track_name:
                            track_playtimes[track_name] += ms_played
                            track_counts[track_name] += 1
                        total_tracks += 1
                        total_ms_played += ms_played
                except json.JSONDecodeError as e:
                    print(f"Warning: Skipping invalid JSON content in {json_file_path}: {e}")
                    continue  # Skip to the next file if JSON is invalid

        except FileNotFoundError:
            print(f"Warning: File not found: {json_file_path}")
            continue  # Skip to the next file if not found

    return artist_playtimes, artist_counts, track_playtimes, track_counts, total_tracks, total_ms_played

def format_timedelta(td):
    """Formats a timedelta object into HH:MM:SS."""
    total_hours = td.days * 24 + td.seconds // 3600
    total_minutes = (td.seconds % 3600) // 60
    total_seconds = td.seconds % 60
    return f"{total_hours:02d}:{total_minutes:02d}:{total_seconds:02d}"

def write_results(output_file, results_type, playtimes, counts, total_tracks, total_ms_played):
    """Writes the analysis results to a file."""

    total_time_played = timedelta(milliseconds=total_ms_played)
    # Filter to top 1000
    ranked_by_time = sorted(playtimes.items(), key=itemgetter(1), reverse=True)[:1000]
    ranked_by_count = counts.most_common(1000) 

    os.makedirs(os.path.dirname(os.path.abspath(output_file)), exist_ok=True)

    with open(output_file, "w", encoding="utf-8") as outfile:
        outfile.write(f"Total Play Count: {total_tracks}\n")
        outfile.write(f"Total Listening Time: {format_timedelta(total_time_played)}\n\n\n")

        outfile.write(f"{results_type} Ranked by Play Count:\n")
        for rank, (item, count) in enumerate(ranked_by_count, 1):
            outfile.write(f"{rank}. {count} times - {item}\n")

        outfile.write(f"\n{results_type} Ranked by Listening Time:\n")
        for rank, (item, ms_played) in enumerate(ranked_by_time, 1):
            playtime = timedelta(milliseconds=ms_played)
            outfile.write(f"{rank}. {format_timedelta(playtime)} - {item}\n")



if __name__ == "__main__":
    json_file_patterns = glob.glob("*.json")
    output_file_tracks = "Results.txt"
    output_file_artists = "Results (Artists).txt"

    artist_playtimes, artist_counts, track_playtimes, track_counts, total_tracks, total_ms_played = analyze_listening_data(json_file_patterns)
    write_results(output_file_artists, "Artists", artist_playtimes, artist_counts, total_tracks, total_ms_played)
    write_results(output_file_tracks, "Songs", track_playtimes, track_counts, total_tracks, total_ms_played)

    print(f"Results saved to {output_file_tracks} and {output_file_artists}")
