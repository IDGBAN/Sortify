# IMPORANT; GNU General Public License v3.0 attached. Copyright (c) 2024 Parsa Rasouli Baghi.
# https://github.com/IDGBAN/Sortify

import json
import os
from collections import Counter, defaultdict
from datetime import datetime, timedelta
from operator import itemgetter
import tkinter as tk
from tkinter import filedialog, messagebox, scrolledtext, ttk

def analyze_listening_data(json_file_paths):
    artist_playtimes = Counter()
    artist_counts = Counter()
    artist_first_playtimes = defaultdict(lambda: datetime.max)
    track_playtimes = Counter()
    track_counts = Counter()
    track_first_playtimes = defaultdict(lambda: datetime.max) 
    track_artist_mapping = {}
    total_tracks = 0
    total_ms_played = 0
    artist_playtimes = Counter()
    artist_counts = Counter()
    artist_first_playtimes = defaultdict(lambda: datetime.max)
    track_playtimes = Counter()
    track_counts = Counter()
    track_first_playtimes = defaultdict(lambda: datetime.max)
    track_artist_mapping = {}
    artist_first_track = {}
    total_tracks = 0
    total_ms_played = 0

    for json_file_path in json_file_paths:
        try:
            with open(json_file_path, 'r', encoding='utf-8') as file:
                if os.stat(json_file_path).st_size == 0:
                    continue

                data = json.load(file)
                for item in data:
                    artist_name = item.get("master_metadata_album_artist_name", "Unknown Artist")
                    track_name = item.get("master_metadata_track_name", "Unknown Track")
                    ms_played = int(item.get("ms_played", 0))
                    timestamp_str = item.get("ts", None)

                    if artist_name and track_name:
                        track_artist_mapping[track_name] = artist_name
                        artist_playtimes[artist_name] += ms_played
                        artist_counts[artist_name] += 1
                        if timestamp_str:
                            playtime = datetime.strptime(timestamp_str, "%Y-%m-%dT%H:%M:%SZ")
                            if playtime < artist_first_playtimes[artist_name]:
                                artist_first_playtimes[artist_name] = playtime
                                artist_first_track[artist_name] = track_name

                    if track_name:
                        track_playtimes[track_name] += ms_played
                        track_counts[track_name] += 1
                        if timestamp_str:
                            playtime = datetime.strptime(timestamp_str, "%Y-%m-%dT%H:%M:%SZ")
                            track_first_playtimes[track_name] = min(track_first_playtimes[track_name], playtime)

                    total_tracks += 1
                    total_ms_played += ms_played

        except FileNotFoundError:
            continue
        except json.JSONDecodeError as e:
            messagebox.showwarning("JSON Error", f"Skipping invalid JSON file: {json_file_path}\nError: {e}")
            continue

    return (artist_playtimes, artist_counts, artist_first_playtimes,
            track_playtimes, track_counts, track_first_playtimes, 
            total_tracks, total_ms_played, track_artist_mapping, artist_first_track)

def format_timedelta(td):
    total_hours = td.days * 24 + td.seconds // 3600
    total_minutes = (td.seconds % 3600) // 60
    total_seconds = td.seconds % 60
    return f"{total_hours:02d}:{total_minutes:02d}:{total_seconds:02d}"

def display_results(results_type, playtimes, counts, first_playtimes, sort_by, sort_direction, track_artist_mapping, artist_first_track=None, search_term=""):
    display_text = ""
    if sort_by == "Time Listened":
        sorted_items = sorted(playtimes.items(), key=itemgetter(1), reverse=(sort_direction == "Descending"))
        display_text = f"{results_type} Ranked by Listening Time:\n"
        for rank, (item, ms_played) in enumerate(sorted_items, 1):
            if search_term.lower() in item.lower():
                playtime = timedelta(milliseconds=ms_played)
                if results_type == "Tracks":
                    artist_name = track_artist_mapping.get(item, "Unknown Artist")
                    display_text += f"{rank}. {format_timedelta(playtime)} - {item} - {artist_name}\n"
                else:
                    display_text += f"{rank}. {format_timedelta(playtime)} - {item}\n"

    elif sort_by == "Counts Listened":
        sorted_items = counts.most_common()
        if sort_direction == "Descending":
            sorted_items = sorted_items
        else:
            sorted_items = sorted_items[::-1]
        display_text = f"{results_type} Ranked by Play Count:\n"
        for rank, (item, count) in enumerate(sorted_items, 1):
            if search_term.lower() in item.lower():
                if results_type == "Tracks":
                    artist_name = track_artist_mapping.get(item, "Unknown Artist")
                    display_text += f"{rank}. {count} times - {item} - {artist_name}\n"
                else:
                    display_text += f"{rank}. {count} times - {item}\n"

    elif sort_by == "First Time Listened":
        sorted_items = sorted(first_playtimes.items(), key=itemgetter(1), reverse=(sort_direction == "Descending"))
        display_text = f"First Play Time of {results_type}:\n"
        for rank, (item, playtime) in enumerate(sorted_items, 1):
            if search_term.lower() in item.lower():
                if results_type == "Artists":
                    first_track = artist_first_track.get(item, "Unknown Track")
                    display_text += f"{rank}. {playtime.strftime('%Y-%m-%d %H:%M')} - {item} - {first_track}\n"
                else:
                    display_text += f"{rank}. {playtime.strftime('%Y-%m-%d %H:%M')} - {item}\n"

    return display_text

def run_analysis():
    json_file_paths = filedialog.askopenfilenames(filetypes=[("JSON files", "*.json")])
    if not json_file_paths:
        return

    sort_menu.config(state="disabled")
    sort_direction_menu.config(state="disabled")
    output_text.config(state="normal")
    output_text.delete(1.0, tk.END)
    output_text.insert(tk.END, "Analyzing data, please wait...\n")
    output_text.config(state="disabled")

    root.update_idletasks()

    global artist_playtimes, artist_counts, artist_first_playtimes
    global track_playtimes, track_counts, track_first_playtimes, total_tracks, total_ms_played, track_artist_mapping, artist_first_track

    (artist_playtimes, artist_counts, artist_first_playtimes,
     track_playtimes, track_counts, track_first_playtimes, 
     total_tracks, total_ms_played, track_artist_mapping, artist_first_track) = analyze_listening_data(json_file_paths)

    if total_tracks == 0:
        output_text.config(state="normal")
        output_text.delete(1.0, tk.END)
        output_text.insert(tk.END, "No valid data found in the selected JSON files.")
        output_text.config(state="disabled")
        return

    update_display("Tracks", track_artist_mapping)
    update_display("Artists", track_artist_mapping)

    output_text.config(state="normal")
    output_text.insert(tk.END, "Analysis complete!\n\n")
    output_text.config(state="disabled")

    sort_menu.config(state="readonly")
    sort_direction_menu.config(state="readonly")

    notebook.select(0)

    track_artist_mapping = {track: artist for track, artist in zip(track_playtimes.keys(), artist_playtimes.keys())}

def update_display(tab_name, track_artist_mapping):
    sort_by = sort_option.get()
    sort_direction = sort_direction_option.get()
    search_term = search_entry.get().strip()
    if tab_name == "Artists":
        results_text = display_results("Artists", artist_playtimes, artist_counts, artist_first_playtimes, sort_by, sort_direction, track_artist_mapping, artist_first_track, search_term)
        artist_text.delete(1.0, tk.END)
        artist_text.insert(tk.END, results_text)
    elif tab_name == "Tracks":
        results_text = display_results("Tracks", track_playtimes, track_counts, track_first_playtimes, sort_by, sort_direction, track_artist_mapping, search_term=search_term)
        track_text.delete(1.0, tk.END)
        track_text.insert(tk.END, results_text)

def save_results():
    save_path = filedialog.asksaveasfilename(defaultextension=".txt", filetypes=[("Text files", "*.txt")])
    output_text.config(state="normal")
    output_text.delete(1.0, tk.END)
    output_text.insert(tk.END, "Saving data...\n")
    output_text.config(state="disabled")
    if save_path:
        with open(save_path, "w", encoding="utf-8") as file:
            file.write("Tracks Ranked by Time Listened:\n")
            sorted_tracks_by_time = sorted(track_playtimes.items(), key=itemgetter(1), reverse=True)
            for rank, (track, ms_played) in enumerate(sorted_tracks_by_time, 1):
                artist_name = track_artist_mapping.get(track, "Unknown Artist")
                playtime = timedelta(milliseconds=ms_played)
                file.write(f"{rank}. {format_timedelta(playtime)} - {track} - {artist_name}\n")
            file.write("\n")

            file.write("Tracks Ranked by Counts Listened:\n")
            sorted_tracks_by_count = sorted(track_counts.items(), key=itemgetter(1), reverse=True)
            for rank, (track, count) in enumerate(sorted_tracks_by_count, 1):
                artist_name = track_artist_mapping.get(track, "Unknown Artist")
                file.write(f"{rank}. {count} times - {track} - {artist_name}\n")
            file.write("\n")

            file.write("Tracks Ranked by First Time Listened:\n")
            sorted_tracks_by_first_playtime = sorted(track_first_playtimes.items(), key=itemgetter(1))
            for rank, (track, first_playtime) in enumerate(sorted_tracks_by_first_playtime, 1):
                artist_name = track_artist_mapping.get(track, "Unknown Artist")
                file.write(f"{rank}. {first_playtime.strftime('%Y-%m-%d %H:%M')} - {track} - {artist_name}\n")
            file.write("\n")

        messagebox.showinfo("Save", f"Results saved to {save_path}")
        output_text.config(state="normal")
        output_text.insert(tk.END, "Data Saved!\n\n")
        output_text.config(state="disabled")

root = tk.Tk()
root.title("Sortify")
root.geometry("1600x900")
root.minsize(740, 610)

style = ttk.Style()
style.configure("TButton", padding=(10, 5))
style.configure("TCombobox", padding=(10, 5))

notebook = ttk.Notebook(root)
notebook.pack(padx=10, pady=10, fill=tk.BOTH, expand=True)

track_frame = ttk.Frame(notebook)
notebook.add(track_frame, text="Tracks")
track_text = scrolledtext.ScrolledText(track_frame, wrap=tk.WORD, width=100, height=30, font=("Arial", 10))
track_text.pack(padx=10, pady=10, fill=tk.BOTH, expand=True)

artist_frame = ttk.Frame(notebook)
notebook.add(artist_frame, text="Artists")
artist_text = scrolledtext.ScrolledText(artist_frame, wrap=tk.WORD, width=100, height=30, font=("Arial", 10))
artist_text.pack(padx=10, pady=10, fill=tk.BOTH, expand=True)

search_label = ttk.Label(root, text="Search:")
search_label.pack(side=tk.LEFT, padx=(10, 5))

search_entry = ttk.Entry(root, width=30)
search_entry.pack(side=tk.LEFT, padx=(5, 10))

sort_option = tk.StringVar()
sort_option.set("Time Listened")
sort_label = ttk.Label(root, text="Sort By:")
sort_label.pack(side=tk.LEFT, padx=(10, 5))

sort_menu = ttk.Combobox(root, 
                         textvariable=sort_option, 
                         values=["Time Listened", "Counts Listened", "First Time Listened"], 
                         state="disabled",
                         style="TCombobox")
sort_menu.pack(side=tk.LEFT, padx=(5, 10))
sort_menu.bind("<<ComboboxSelected>>", lambda event: update_display("Tracks", track_artist_mapping) or update_display("Artists", track_artist_mapping))

sort_direction_option = tk.StringVar()
sort_direction_option.set("Descending")
sort_direction_label = ttk.Label(root, text="Sort Direction:")
sort_direction_label.pack(side=tk.LEFT, padx=(10, 5))

sort_direction_menu = ttk.Combobox(root, 
                                    textvariable=sort_direction_option, 
                                    values=["Ascending", "Descending"], 
                                    state="disabled",
                                    style="TCombobox")
sort_direction_menu.pack(side=tk.LEFT, padx=(5, 10))
sort_menu.bind("<<ComboboxSelected>>", lambda event: update_display("Tracks", track_artist_mapping) or update_display("Artists", track_artist_mapping))
sort_direction_menu.bind("<<ComboboxSelected>>", lambda event: update_display("Tracks", track_artist_mapping) or update_display("Artists", track_artist_mapping))

run_button = ttk.Button(root, text="Run Analysis", command=run_analysis, style="TButton")
run_button.pack(side=tk.LEFT, padx=10, pady=10)

save_button = ttk.Button(root, text="Save Results", command=save_results, style="TButton")
save_button.pack(side=tk.LEFT, padx=10, pady=10)

output_text = scrolledtext.ScrolledText(root, wrap=tk.WORD, width=50, height=10, font=("Arial", 10))
output_text.pack(side=tk.BOTTOM, padx=10, pady=10, fill=tk.BOTH, expand=True)
output_text.config(state="disabled")

root.mainloop()
