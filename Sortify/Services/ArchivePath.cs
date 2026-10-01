using System.IO;

namespace Sortify.Services;

/// <summary>
/// Names a file inside a ZIP export as one string ("C:\my_spotify_data.zip|folder/file.json"),
/// so an archive entry can travel through the same file lists, cache keys and reloads as a
/// file on disk. Windows forbids the separator in paths, so it never appears in a real one.
/// </summary>
public static class ArchivePath
{
    private const char Separator = '|';

    /// <summary>True for a path that names a ZIP file itself.</summary>
    public static bool IsArchive(string? path) =>
        !string.IsNullOrEmpty(path) &&
        path.IndexOf(Separator) < 0 &&
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    public static string Combine(string archive, string entry) => archive + Separator + entry;

    /// <summary>Splits an entry path back into the archive and the entry name inside it.</summary>
    public static bool TrySplit(string path, out string archive, out string entry)
    {
        int at = path.IndexOf(Separator);
        if (at <= 0 || at == path.Length - 1)
        {
            archive = string.Empty;
            entry = string.Empty;
            return false;
        }

        archive = path[..at];
        entry = path[(at + 1)..];
        return true;
    }
}
