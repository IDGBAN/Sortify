def write_results(output_file, results_type, playtimes_by_year, counts_by_year, total_tracks_by_year,
                 total_ms_played_by_year, first_playtimes_by_year=None):
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

        # Handle first_playtimes based on results_type
        if results_type == "Artists":
            first_playtimes = None  # No first playtime for artists
        else:
            first_playtimes = first_playtimes_by_year.get(year, None)  # For songs

        # Construct the output file name with a timestamp
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        output_file_year = f"{output_file}_{year}_{timestamp}.txt"

        # Create the directory if it doesn't exist
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

    write_results(
        output_file_artists,
        "Artists",
        artist_playtimes,
        artist_counts,
        total_tracks_by_year,
        total_ms_played_by_year,
        None,
    )  # Pass None for first_playtimes_by_year for artists

    write_results(output_file_tracks, "Songs", track_playtimes_by_year, track_counts_by_year,
                  total_tracks_by_year, total_ms_played_by_year, track_first_playtimes_by_year)


    print(f"Results saved to separate files for each year with timestamps.")
