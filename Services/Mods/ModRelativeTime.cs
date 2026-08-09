namespace QuiverLauncher.Services.Mods;

public static class ModRelativeTime
{
    public static string Format(long? unixSecondsUtc, DateTimeOffset? now = null)
    {
        if (unixSecondsUtc is null or <= 0)
            return string.Empty;

        var when = DateTimeOffset.FromUnixTimeSeconds(unixSecondsUtc.Value);
        var current = now ?? DateTimeOffset.UtcNow;
        var elapsed = current - when;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (elapsed.TotalMinutes < 1)
            return "just now";
        if (elapsed.TotalMinutes < 60)
        {
            var minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }

        if (elapsed.TotalHours < 24)
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        if (elapsed.TotalDays < 2)
            return "yesterday";

        if (elapsed.TotalDays < 30)
        {
            var days = (int)elapsed.TotalDays;
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }

        if (elapsed.TotalDays < 60)
            return "last month";

        if (elapsed.TotalDays < 365)
        {
            var months = (int)(elapsed.TotalDays / 30);
            return months <= 1 ? "1 month ago" : $"{months} months ago";
        }

        var years = (int)(elapsed.TotalDays / 365);
        return years <= 1 ? "1 year ago" : $"{years} years ago";
    }

    public static string FormatCompactCount(long value)
    {
        if (value < 0)
            value = 0;

        return value switch
        {
            < 1_000 => value.ToString(),
            < 10_000 => $"{value / 1000.0:0.#}k",
            < 1_000_000 => $"{value / 1000}k",
            < 10_000_000 => $"{value / 1_000_000.0:0.#}M",
            _ => $"{value / 1_000_000}M",
        };
    }
}
