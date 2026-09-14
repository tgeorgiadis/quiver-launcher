using Avalonia;

namespace QuiverLauncher.Services;

internal static class InterfaceScale
{
    public static IReadOnlyList<int> Percentages { get; } = Array.AsReadOnly(new[] { 50, 75, 100, 125, 150, 175, 200, 225 });
    public static int Normalize(int percent) => Percentages.Contains(percent) ? percent : 100;

    public static int Fit(int requested, Size availableClientArea) => Percentages
        .Where(p => p <= Normalize(requested) && 750 * p / 100d <= availableClientArea.Width
            && 490 * p / 100d <= availableClientArea.Height)
        .DefaultIfEmpty(Percentages[0]).Max();
}
