namespace QuiverLauncher.Services;

public static class BackgroundUpdateCheckIntervals
{
    public static readonly int[] Presets = [15, 30, 60, 180, 360, 720, 1440];

    public const int DefaultMinutes = 60;

    public static int Normalize(int minutes) =>
        Presets.Contains(minutes) ? minutes : DefaultMinutes;

    public static string FormatLabel(int minutes) => Normalize(minutes) switch
    {
        15 => "Every 15 minutes",
        30 => "Every 30 minutes",
        60 => "Every hour",
        180 => "Every 3 hours",
        360 => "Every 6 hours",
        720 => "Every 12 hours",
        1440 => "Every day",
        _ => $"Every {Normalize(minutes)} minutes",
    };
}
