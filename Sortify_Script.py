import json
import os
import glob
from collections import Counter
from datetime import timedelta
from operator import itemgetter

def analyze_listening_data(json_file_paths, aggregate_by='artist'):
    """Analyzes listening data from multiple JSON files, aggregating by artist or track."""
    playtimes = Counter()
    counts = Counter()
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
                        if aggregate_by == 'artist':
                            key = item.get("master_metadata_album_artist_name", "Unknown Artist")
                        elif aggregate_by == 'track':
                            key = item.get("master_metadata_track_name", "Unknown Track")
                        else:
                            raise ValueError(f"Invalid aggregation type: {aggregate_by}")

                        ms_played = int(item.get("ms_played", 0))
                        if key:
                            playtimes[key] += ms_played
                            counts[key] += 1
                            total_tracks += 1
                            total_ms_played += ms_played
                except json.JSONDecodeError as e:
                    print(f"Warning: Skipping invalid JSON content in {json_file_path}: {e}")
                    continue  # Skip to the next file if JSON is invalid

        except FileNotFoundError:
            print(f"Warning: File not found: {json_file_path}")
            continue  # Skip to the next file if not found

    return playtimes, counts, total_tracks, total_ms_played

def format_timedelta(td):  
    """Formats a timedelta object into HH:MM:SS."""  
    total_hours = td.days * 24 + td.seconds // 3600
    total_minutes = (td.seconds % 3600) // 60
    total_seconds = td.seconds % 60
    return f"{total_hours:02d}:{total_minutes:02d}:{total_seconds:02d}"

def write_results(output_file, results_type, playtimes, counts, total_tracks, total_ms_played):
    """Writes the analysis results to a file."""
    total_time_played = timedelta(milliseconds=total_ms_played)
    ranked_by_time = sorted(playtimes.items(), key=itemgetter(1), reverse=True)
    ranked_by_count = counts.most_common()

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
    output_file_tracks = "Results (Tracks).txt"
    output_file_artists = "Results (Artists).txt"

    # Analyze by artist
    artist_playtimes, artist_counts, total_tracks, total_ms_played = analyze_listening_data(
        json_file_patterns, aggregate_by='artist'
    )
    write_results(output_file_artists, "Artists", artist_playtimes, artist_counts, total_tracks, total_ms_played)

    # Analyze by track
    track_playtimes, track_counts, total_tracks, total_ms_played = analyze_listening_data(
        json_file_patterns, aggregate_by='track'
    )
    write_results(output_file_tracks, "Tracks", track_playtimes, track_counts, total_tracks, total_ms_played)

    print(f"Results saved to {output_file_tracks} and {output_file_artists}")
