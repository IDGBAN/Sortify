using System.IO;

namespace Sortify.Services;

/// <summary>Where Sortify keeps its own files: settings, the parsed-history cache and the error log.</summary>
public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\Sortify, created on demand. Tests point this at a temp folder.</summary>
    public static string DataDirectory { get; internal set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sortify");

    public static string ErrorLog => Path.Combine(DataDirectory, "error.log");
}
