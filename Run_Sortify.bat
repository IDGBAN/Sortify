@echo off
echo Downloading script...
powershell -Command "(New-Object System.Net.WebClient).DownloadFile('https://raw.githubusercontent.com/IDGBAN/Sortify/main/Sortify_Script.py', 'Sortify_Script.py')"
echo Script downloaded!
echo Running script...
echo:
python Sortify_Script.py
echo:
echo Deleting script...
del Sortify_Script.py
echo:
echo Analysis Successful!
echo:
echo:
timeout /t 10
