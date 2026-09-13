namespace QuiverLauncher.Services;

/// <summary>Allow package-manager state to settle after Android's result callback.</summary>
public static class AndroidPackageReadiness
{
    public static async Task<bool> WaitAsync(Func<bool> ready, Func<Task>? delay = null)
    {
        delay ??= () => Task.Delay(250);
        for (var attempt = 0; attempt < 9; attempt++)
        {
            if (ready()) return true;
            if (attempt < 8) await delay().ConfigureAwait(false);
        }
        return false;
    }
}
