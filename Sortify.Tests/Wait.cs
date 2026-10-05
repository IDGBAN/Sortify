namespace Sortify.Tests;

/// <summary>Waits on work a test set off in the background, for as long as it takes and no longer.</summary>
internal static class Wait
{
    /// <summary>Generous, since a slow build agent can take seconds over work that takes milliseconds here.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Returns once <paramref name="done"/> holds, or when the time runs out, leaving the test's
    /// own assertions to say what didn't happen. On the WPF test thread the dispatcher keeps
    /// running in between, so the work being waited on can finish.
    /// </summary>
    public static async Task UntilAsync(Func<bool> done)
    {
        var giveUp = DateTime.UtcNow + Limit;
        while (!done() && DateTime.UtcNow < giveUp)
            await Task.Delay(20);
    }
}
