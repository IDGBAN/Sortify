# AGENTS.md

## Cursor Cloud specific instructions

Sortify is a **single .NET 8 WPF desktop app** (`Sortify/Sortify.csproj`, `net8.0-windows`, `UseWPF=true`). It has no backend, database, or network services — it reads local Spotify extended-history JSON files and computes listening stats. See `README.md` for the product overview and the canonical `dotnet run` / `dotnet publish` commands.

### Key platform limitation (important)
- The Cloud VM is **Linux**, but WPF is **Windows-only**. The GUI **cannot launch here**: running the built app fails with `Microsoft.WindowsDesktop.App ... No frameworks were found`. There is no way to run the interactive UI on this VM.
- Because the project targets `net8.0-windows`, `dotnet restore/build/publish` on Linux require the extra property `-p:EnableWindowsTargeting=true` (otherwise you get `NETSDK1100`). This flag lets the build compile and even cross-publish the real `win-x64` `Sortify.exe`.

### Build / publish (work on Linux with the flag)
- Build (dev): `dotnet build Sortify/Sortify.csproj -c Debug -p:EnableWindowsTargeting=true`
- Publish real Windows exe: `dotnet publish Sortify/Sortify.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true` → `Sortify/bin/Release/net8.0-windows/win-x64/publish/Sortify.exe`
- The `NU1701` warnings about OpenTK/SkiaSharp being restored against `.NETFramework` are expected and non-fatal.

### Lint / tests
- There is no separate linter and **no automated test project**. The nullable-enabled C# compiler run via `dotnet build` is the only static check.

### Testing the core logic without the GUI
- Everything under `Sortify/Models/` and `Sortify/Services/` (except `ChartBuilder.cs`, which needs LiveCharts/WPF) is pure cross-platform C#. To exercise the real parse → filter → analyze → export flow on Linux, create a throwaway console project targeting `net8.0` that `<Compile Include>`s those source files and calls `HistoryParser` + `AnalysisEngine` + `ExportService`. This validates the app's core behavior even though the WPF UI can't run here.
