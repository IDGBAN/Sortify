import re
import os
from collections import Counter

def count_track_names_from_files(json_file_paths, output_file):
    all_track_names = []

    for json_file_path in json_file_paths:
        print(f"Processing file: {json_file_path}")

        with open(json_file_path, 'r', encoding='utf-8') as file:
            for line in file:
                matches = re.findall(r'"master_metadata_track_name"\s*:\s*"([^"]*)"', line)
                all_track_names.extend(matches)

    track_counts = Counter(all_track_names)
    ranked_counts = track_counts.most_common()

    # Create a new output file
    output_directory = os.path.dirname(output_file)  # Get directory of output file
    if not os.path.exists(output_directory):  # Create directory if it doesn't exist
        os.makedirs(output_directory)

    with open(output_file, "w", encoding="utf-8") as outfile:
        outfile.write("\nOverall Track Name Counts (Most to Least):\n")
        for track_name, count in ranked_counts:
            outfile.write(f"- '{track_name}': {count} times\n")

    return ranked_counts

if __name__ == "__main__":
    script_directory = os.path.dirname(os.path.abspath(__file__))
    json_files = [f for f in os.listdir(script_directory) if f.endswith('.json')]
    
    output_file = os.path.join(script_directory, "track_counts.txt")
   
    ranked_counts = count_track_names_from_files(json_files, output_file)

    print(f"\nResults saved to {output_file}")



#       python track_counter_multiple.py 2022-2023.json 2023-2024.json 2023-2024-1.json 2023-2024-2.json output.txt