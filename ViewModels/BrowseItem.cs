using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

/// <summary>One Browse card: a quiverlauncher.com app, or an app from the player's own list.</summary>
public sealed class BrowseItem : ObservableViewModel
{
    private bool _inLibrary;
    private bool _isGamepadFocused;

    private BrowseItem(QuiverCatalogApp? app, GameInfo? listApp, string title, string subtitle, string? imageUrl, string kind)
    {
        App = app;
        ListApp = listApp;
        Title = title;
        Subtitle = subtitle;
        ImageUrl = imageUrl;
        Kind = kind;
    }

    /// <summary>The catalog app, or null for an app from the player's own list.</summary>
    public QuiverCatalogApp? App { get; }
    /// <summary>The app from the player's own list, already in library format.</summary>
    public GameInfo? ListApp { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string? ImageUrl { get; }
    public string Kind { get; }
    public bool HasSubtitle => Subtitle.Length > 0;
    public bool HasKind => Kind.Length > 0;
    public string FolderName => App?.Launcher.FolderName ?? ListApp?.FolderName ?? "";
    public string ScoreText => App == null ? "" : BrowseText.Score(App.Recommended, App.ReportIssues, App.ReportBroken);
    public string ReleaseText => App == null ? "" : BrowseText.ReleaseAge(App.LastReleaseAt, DateTimeOffset.UtcNow);
    public bool IsNew => App != null && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(App.AddedAt) < TimeSpan.FromDays(30);
    public string Badge => InLibrary ? "In library" : IsNew ? "New" : "";
    public bool HasBadge => Badge.Length > 0;

    public bool InLibrary
    {
        get => _inLibrary;
        set
        {
            if (Set(ref _inLibrary, value))
            {
                Notify(nameof(Badge));
                Notify(nameof(HasBadge));
            }
        }
    }

    public bool IsGamepadFocused { get => _isGamepadFocused; set => Set(ref _isGamepadFocused, value); }

    public static BrowseItem FromCatalog(QuiverCatalogApp app, IReadOnlyDictionary<string, string> consoleNames)
    {
        var games = string.Join(", ", app.Games.Select(g => g.Title).Where(t => !string.IsNullOrWhiteSpace(t)));
        var kind = app.Consoles.Take(2).Select(id => consoleNames.GetValueOrDefault(id, id))
            .Append(BrowseText.ProjectTypeName(app.ProjectType)).Where(s => s.Length > 0);
        var art = app.LibraryArt;
        return new(app, null, FirstText(app.ProjectName, app.Name), games.Length > 0 ? games : app.Name,
            FirstText(art?.Header, art?.Capsule, app.Artwork), string.Join(" · ", kind));
    }

    public static BrowseItem FromList(GameInfo app)
    {
        var title = FirstText(app.Project, app.Name);
        var subtitle = string.Equals(title, app.Name, StringComparison.OrdinalIgnoreCase) ? "" : app.Name;
        return new(null, app, title, subtitle, FirstText(app.GameIconUrl), string.Join(" · ", app.Tags.Take(2)));
    }

    private static string FirstText(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}

/// <summary>How Browse words catalog data, matching the website and Quiver Launcher 4.</summary>
public static class BrowseText
{
    public static readonly IReadOnlyList<(string? Id, string Name)> Sorts =
        [("added", "Recently added"), ("updated", "Recently updated"), ("rating", "Top rated"), ("name", "Name (A-Z)")];
    public static readonly IReadOnlyList<(string? Id, string Name)> Platforms =
        [(null, "All platforms"), ("windows", "Windows"), ("linux", "Linux"), ("macos", "macOS"), ("android", "Android"), ("ios", "iOS")];
    public static readonly IReadOnlyList<(string? Id, string Name)> ProjectTypes =
        [(null, "All types"), ("port", "Port"), ("tool", "Tool"), ("emulator", "Emulator"), ("game", "Standalone game")];

    public static string? CurrentPlatform =>
        OperatingSystem.IsAndroid() ? "android" : OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : null;

    public static string ProjectTypeName(string? id) =>
        string.IsNullOrEmpty(id) ? "" : ProjectTypes.FirstOrDefault(t => t.Id == id).Name ?? "";

    public static string PlatformNames(IEnumerable<string> ids) => string.Join(", ",
        ids.Select(id => Platforms.FirstOrDefault(p => p.Id == id).Name).Where(n => n != null));

    /// <summary>What players said about how an app runs, in one line.</summary>
    public static string Score(int runs, int issues, int broken)
    {
        if (runs + issues + broken == 0) return "Not rated yet";
        var max = Math.Max(runs, Math.Max(issues, broken));
        var label = new[] { runs, issues, broken }.Count(n => n == max) > 1 ? "Mixed"
            : runs == max ? "Mostly runs" : issues == max ? "Mostly runs with issues" : "Mostly doesn't run";
        var said = new List<string>();
        if (runs > 0) said.Add($"{runs} {(runs == 1 ? "runs" : "run")} well");
        if (issues > 0) said.Add($"{issues} with issues");
        if (broken > 0) said.Add($"{broken} {(broken == 1 ? "doesn't" : "don't")} run");
        return $"{label} · {string.Join(", ", said)}";
    }

    public static string ReleaseAge(long? releasedAt, DateTimeOffset now)
    {
        if (releasedAt is not { } at) return "No releases";
        var age = now - DateTimeOffset.FromUnixTimeMilliseconds(at);
        if (age.TotalDays >= 365) return $"No release in {(int)(age.TotalDays / 365)} yr+";
        return "Updated " + (age.TotalDays >= 30 ? $"{(int)(age.TotalDays / 30)} mo ago"
            : age.TotalDays >= 1 ? $"{(int)age.TotalDays} d ago"
            : age.TotalHours >= 1 ? $"{(int)age.TotalHours} h ago" : "just now");
    }

    public static string ReviewResult(string result) => result switch
    {
        "runs" => "Runs well",
        "issues" => "Runs with issues",
        "broken" => "Doesn't run",
        _ => result,
    };
}
