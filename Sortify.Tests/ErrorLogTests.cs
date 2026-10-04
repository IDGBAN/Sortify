using System.IO;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class ErrorLogTests : IDisposable
{
    public ErrorLogTests() => Dispose();

    public void Dispose()
    {
        File.Delete(AppPaths.ErrorLog);
        File.Delete(ErrorLog.OldLog);
    }

    [Fact]
    public void AppendsTheException()
    {
        ErrorLog.TryWrite(new InvalidOperationException("first"));
        ErrorLog.TryWrite(new InvalidOperationException("second"));

        string log = File.ReadAllText(AppPaths.ErrorLog);
        Assert.Contains("first", log);
        Assert.Contains("second", log);
    }

    [Fact]
    public void StartsOverOnceTheLogIsFull()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(AppPaths.ErrorLog, new string('x', (int)ErrorLog.MaxBytes));

        ErrorLog.TryWrite(new InvalidOperationException("fresh"));

        Assert.Contains("fresh", File.ReadAllText(AppPaths.ErrorLog));
        Assert.True(new FileInfo(AppPaths.ErrorLog).Length < ErrorLog.MaxBytes);
        Assert.Equal(ErrorLog.MaxBytes, new FileInfo(ErrorLog.OldLog).Length);
    }
}
