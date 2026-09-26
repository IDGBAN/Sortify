using System.IO;
using System.Runtime.CompilerServices;
using Sortify.Services;

namespace Sortify.Tests;

internal static class TestDataDirectory
{
    // Runs before any test touches the settings file or the record cache, so a test run
    // never reads or clobbers the developer's real %LOCALAPPDATA%\Sortify.
    [ModuleInitializer]
    internal static void Redirect()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Sortify.Tests", Environment.ProcessId.ToString());
        Directory.CreateDirectory(dir);
        AppPaths.DataDirectory = dir;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
