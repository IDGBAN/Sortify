using System.IO;
using System.Runtime.CompilerServices;
using Sortify.Services;
using Xunit;

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

/// <summary>
/// Tests that read or write the files in the data folder: the settings, the record cache and the
/// error log. A test run has one of each, so these tests take turns.
/// </summary>
[CollectionDefinition(Name)]
public class DataFolderCollection
{
    public const string Name = "Data folder";
}
