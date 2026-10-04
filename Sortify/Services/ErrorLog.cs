using System.Globalization;
using System.IO;

namespace Sortify.Services;

/// <summary>Appends unexpected exceptions to <see cref="AppPaths.ErrorLog"/>.</summary>
public static class ErrorLog
{
    /// <summary>
    /// Past this size the log is moved aside to error.old.log and a fresh one started, so an
    /// error that repeats on every launch can't grow it without end.
    /// </summary>
    internal const long MaxBytes = 1024 * 1024;

    // Unobserved task exceptions arrive on the finalizer thread, alongside UI-thread ones.
    private static readonly object Gate = new();

    internal static string OldLog => Path.Combine(AppPaths.DataDirectory, "error.old.log");

    public static void TryWrite(Exception exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                var log = new FileInfo(AppPaths.ErrorLog);
                if (log.Exists && log.Length >= MaxBytes)
                    File.Move(log.FullName, OldLog, overwrite: true);

                string when = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                File.AppendAllText(AppPaths.ErrorLog, $"[{when}] {exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the thing that brings the app down.
        }
    }
}
