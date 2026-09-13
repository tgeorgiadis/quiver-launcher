namespace QuiverLauncher.Android;

internal static class AndroidPackageOperations
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);
}
