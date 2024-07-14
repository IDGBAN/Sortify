import json
import os
from collections import Counter
from datetime import timedelta
from operator import itemgetter
import glob

def analyze_listening_data(json_file_paths, output_file):
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

                file_content = file.read()
                try:
                    data = json.loads(file_content)
                    for item in data:
                        track_name = item.get("master_metadata_track_name")
                        ms_played = int(item.get("ms_played", 0))
                        if track_name:
                            track_playtimes[track_name] += ms_played
                            track_counts[track_name] += 1
                            total_tracks += 1
                            total_ms_played += ms_played
                except json.JSONDecodeError as e:
                    print(f"Warning: Skipping invalid JSON content in {json_file_path}: {e}")
                    continue

        except FileNotFoundError:
            print(f"Warning: File not found: {json_file_path}")
            continue

    total_time_played = timedelta(milliseconds=total_ms_played)

    # Calculate total listening time in hours, minutes, and seconds, including days
    total_hours = total_time_played.days * 24 + total_time_played.seconds // 3600
    total_minutes = (total_time_played.seconds % 3600) // 60
    total_seconds = total_time_played.seconds % 60
    formatted_total_time = f"{total_hours:02d}:{total_minutes:02d}:{total_seconds:02d}"

    # Rank tracks by play time (descending order)
    ranked_by_time = sorted(track_playtimes.items(), key=itemgetter(1), reverse=True)

    # Rank tracks by play count (descending order)
    ranked_by_count = track_counts.most_common()

    # Save results to the output file
    output_directory = os.path.dirname(os.path.abspath(output_file))
    os.makedirs(output_directory, exist_ok=True)

    with open(output_file, "w", encoding="utf-8") as outfile:
        # Summary at the top
        outfile.write(f"Total Play Count: {total_tracks}\n")
        outfile.write(f"Total Listening Time: {formatted_total_time}\n\n\n")

        # Track Play Count Ranking
        outfile.write("Songs Ranked by Play Count:\n")
        for track_name, count in ranked_by_count:
            outfile.write(f"- '{track_name}': {count} times\n")
        outfile.write("\n\n")  # Add a newline for separation

        # Track Listening Time Ranking
        outfile.write("Songs Ranked by Listening Time:\n")
        for track_name, ms_played in ranked_by_time:
            playtime = timedelta(milliseconds=ms_played)
            # Calculate individual track times in hours, minutes, and seconds, including days
            play_hours = playtime.days * 24 + playtime.seconds // 3600
            play_minutes = (playtime.seconds % 3600) // 60
            play_seconds = playtime.seconds % 60
            formatted_playtime = f"{play_hours:02d}:{play_minutes:02d}:{play_seconds:02d}"
            outfile.write(f"- '{track_name}': {formatted_playtime}\n")

    return ranked_by_time

if __name__ == "__main__":
    json_file_patterns = glob.glob("*.json")  # Get all JSON files in the directory
    output_file = "Results.txt"
    ranked_counts = analyze_listening_data(json_file_patterns, output_file)

    print(f"\nResults saved to {output_file}")
