@echo off
echo Downloading script...
powershell -Command "(New-Object System.Net.WebClient).DownloadFile('https://raw.githubusercontent.com/IDGBAN/Sortify/main/Sortify_Script.py', 'Sortify_Script.py')"
echo Script downloaded!
echo Running script...
python Sortify_Script.py
echo Deleting script...
del Sortify_Script.py
echo Process Successful!
timeout /t 10
