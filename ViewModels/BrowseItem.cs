using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

/// <summary>One App Catalog card: a quiverlauncher.com app, or an app from the player's own list.</summary>
public sealed class BrowseItem : ObservableViewModel, IBrowseCard
{
    private bool _inLibrary;
    private bool _isGamepadFocused;
    private bool _isHovered;

    private BrowseItem(QuiverCatalogApp? app, GameInfo? listApp, string title, string subtitle, string? imageUrl, string kind,
        string cardKind, IReadOnlyList<string> basedOn)
    {
        App = app;
        ListApp = listApp;
        Title = title;
        Subtitle = subtitle;
        ImageUrl = imageUrl;
        Kind = kind;
        CardKind = cardKind.ToUpperInvariant();
        BasedOn = basedOn;
        var os = app?.SupportedOS ?? [];
        RunsOnWindows = os.Contains("windows");
        RunsOnMacOS = os.Contains("macos");
        RunsOnLinux = os.Contains("linux");
        RunsOnAndroid = os.Contains("android");
        RunsOnIOS = os.Contains("ios");
        // The website lists platforms in its icon order.
        PlatformTip = BrowseText.PlatformNames(new[] { "windows", "macos", "linux", "android", "ios" }.Where(os.Contains));
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

    // The card, drawn like the website's: tags and platforms, title, "Based on" games, then how it runs and when it updated.
    /// <summary>The app's first two tags, consoles by name, in capitals.</summary>
    public string CardKind { get; }
    public IReadOnlyList<string> BasedOn { get; }
    public bool HasBasedOn => BasedOn.Count > 0;
    public bool RunsOnWindows { get; }
    public bool RunsOnMacOS { get; }
    public bool RunsOnLinux { get; }
    public bool RunsOnAndroid { get; }
    public bool RunsOnIOS { get; }
    public string PlatformTip { get; }
    public string AiChip => App?.AiLevel switch { "assisted" => "AI-ASSISTED", "generated" => "MOSTLY AI", _ => "" };
    public bool HasAiChip => AiChip.Length > 0;
    public bool HasFooter => App != null;
    public string ScoreLabel => App == null ? "" : BrowseText.ScoreParts(App.Recommended, App.ReportIssues, App.ReportBroken).Label;
    public string ScoreCounts => App == null ? "" : BrowseText.ScoreParts(App.Recommended, App.ReportIssues, App.ReportBroken).Counts;
    public string ScoreTone => App == null ? "" : BrowseText.ScoreParts(App.Recommended, App.ReportIssues, App.ReportBroken).Tone;
    public bool ScorePositive => ScoreTone == "positive";
    public bool ScoreCaution => ScoreTone == "caution";
    public bool ScoreNegative => ScoreTone == "negative";
    public bool ScoreMuted => ScoreTone == "muted";
    public bool ReleaseStale => App != null && ReleaseText.StartsWith("No release", StringComparison.Ordinal);
    public string FolderName => App != null ? QuiverCatalogMapping.FolderFor(App) : ListApp?.FolderName ?? "";
    public string ScoreText => App == null ? "" : BrowseText.Score(App.Recommended, App.ReportIssues, App.ReportBroken);
    public string ReleaseText => App == null ? "" : BrowseText.ReleaseAge(App.LastReleaseAt, DateTimeOffset.UtcNow);
    public bool IsNew => App != null && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds((long)App.AddedAt) < TimeSpan.FromDays(30);
    public string Badge => InLibrary ? "In library" : IsNew ? "New" : "";
    public bool HasBadge => Badge.Length > 0;
    public string BadgeText => Badge.ToUpperInvariant();

    public bool InLibrary
    {
        get => _inLibrary;
        set
        {
            if (Set(ref _inLibrary, value))
            {
                Notify(nameof(Badge));
                Notify(nameof(BadgeText));
                Notify(nameof(HasBadge));
                Notify(nameof(CanAdd));
                Notify(nameof(ShowCardAction));
                Notify(nameof(CardActionText));
                Notify(nameof(CardActionTip));
            }
        }
    }

    private bool _isAdding;
    /// <summary>The card's Add button is adding it to the library.</summary>
    public bool IsAdding
    {
        get => _isAdding;
        set
        {
            if (Set(ref _isAdding, value))
            {
                Notify(nameof(AddText));
                Notify(nameof(CardActionText));
            }
        }
    }

    /// <summary>The card can be added to the library straight from the catalog, without opening its page.</summary>
    public bool CanAdd => !InLibrary && (App != null || ListApp != null);
    public string AddText => IsAdding ? "Adding…" : "+ Add";
    /// <summary>The card's button: + Add, or Open in Library once the app is in the library.</summary>
    public bool ShowCardAction => CanAdd || (InLibrary && (App != null || ListApp != null));
    public string CardActionText => InLibrary && !IsAdding ? "Open in Library" : AddText;
    public string CardActionTip => InLibrary ? "Show it in your library" : "Add to your library";

    public bool IsGamepadFocused
    {
        get => _isGamepadFocused;
        set { if (Set(ref _isGamepadFocused, value)) Notify(nameof(ShouldScroll)); }
    }

    public bool IsHovered
    {
        get => _isHovered;
        set { if (Set(ref _isHovered, value)) Notify(nameof(ShouldScroll)); }
    }

    /// <summary>Cut-off text scrolls while the card is hovered or highlighted, as on the website.</summary>
    public bool ShouldScroll => IsHovered || IsGamepadFocused;

    public static BrowseItem FromCatalog(QuiverCatalogApp app, IReadOnlyDictionary<string, string> consoleNames)
    {
        var games = string.Join(", ", app.Games.Select(g => g.Title).Where(t => !string.IsNullOrWhiteSpace(t)));
        var kind = app.Consoles.Take(2).Select(id => consoleNames.GetValueOrDefault(id, id))
            .Append(BrowseText.ProjectTypeName(app.ProjectType)).Where(s => s.Length > 0);
        var art = app.LibraryArt;
        // The website's card shows the first two tags, a console tag by the console's name.
        var tags = app.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Take(2)
            .Select(t => consoleNames.GetValueOrDefault(t.Trim(), BrowseText.TagLabel(t.Trim()))).ToList();
        var cardKind = tags.Count > 0 ? string.Join(" · ", tags) : string.Join(" · ", kind);
        var basedOn = app.Games.Select(g => g.Title).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        return new(app, null, FirstText(app.ProjectName, app.Name), games.Length > 0 ? games : app.Name,
            FirstText(art?.Header, art?.Capsule, app.Artwork), string.Join(" · ", kind), cardKind, basedOn);
    }

    public static BrowseItem FromList(GameInfo app)
    {
        var title = FirstText(app.Project, app.Name);
        var subtitle = string.Equals(title, app.Name, StringComparison.OrdinalIgnoreCase) ? "" : app.Name;
        var kind = string.Join(" · ", app.Tags.Take(2));
        return new(null, app, title, subtitle, FirstText(app.GameIconUrl), kind, kind, subtitle.Length > 0 ? [subtitle] : []);
    }

    private static string FirstText(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}

/// <summary>How Browse words catalog data, matching the website and Quiver Launcher 4.</summary>
public static class BrowseText
{
    public static readonly IReadOnlyList<(string? Id, string Name)> Sorts =
        [("added", "Recently added"), ("updated", "Recently updated"), ("rating", "Top rated"), ("name", "Name A–Z")];
    public static readonly IReadOnlyList<(string? Id, string Name)> Platforms =
        [(null, "All platforms"), ("windows", "Windows"), ("linux", "Linux"), ("macos", "macOS"), ("android", "Android"), ("ios", "iOS")];
    public static readonly IReadOnlyList<(string? Id, string Name)> ProjectTypes =
        [(null, "All project types"), ("port", "Port"), ("tool", "Tool"), ("emulator", "Emulator"), ("game", "Standalone game")];

    public static readonly IReadOnlyList<(string? Id, string Name)> AiFilters =
        [(null, "Show all apps"), ("no-generated", "Hide mostly AI-generated apps"), ("no-ai", "Hide apps with any AI use")];

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
        var (label, counts, _) = ScoreParts(runs, issues, broken);
        return label + counts;
    }

    /// <summary>The verdict, the counts behind it (" · 2 run well"), and its tone, as the website colours them.</summary>
    public static (string Label, string Counts, string Tone) ScoreParts(int runs, int issues, int broken)
    {
        if (runs + issues + broken == 0) return ("Not rated yet", "", "muted");
        // Always one verdict, as ProtonDB always shows one tier: what most players said, and when it's a tie the more
        // careful of the answers tied (the counts after it show the split).
        var max = Math.Max(runs, Math.Max(issues, broken));
        var (label, tone) = broken == max ? ("Doesn't run", "negative") : issues == max ? ("Has issues", "caution") : ("Runs well", "positive");
        // Everyone agrees: "Runs well · 4 players", rather than repeating the verdict.
        var total = runs + issues + broken;
        if (new[] { runs, issues, broken }.Count(n => n > 0) == 1)
            return (label, $" · {total} {(total == 1 ? "player" : "players")}", tone);
        var said = new List<string>();
        if (runs > 0) said.Add($"{runs} {(runs == 1 ? "runs" : "run")} well");
        if (issues > 0) said.Add($"{issues} with issues");
        if (broken > 0) said.Add($"{broken} {(broken == 1 ? "doesn't" : "don't")} run");
        return (label, " · " + string.Join(", ", said), tone);
    }

    // Tags are stored lowercase for matching; these read as the website writes them.
    private static readonly Dictionary<string, string> SpecialTagWords = new()
    {
        ["3ds"] = "3DS", ["gb"] = "GB", ["gba"] = "GBA", ["gbc"] = "GBC", ["gcn"] = "GCN", ["n64"] = "N64", ["nds"] = "NDS",
        ["nes"] = "NES", ["snes"] = "SNES", ["pc"] = "PC", ["ps1"] = "PS1", ["ps2"] = "PS2", ["psp"] = "PSP", ["smd"] = "SMD",
        ["ufc"] = "UFC", ["wwe"] = "WWE", ["wwf"] = "WWF", ["wcw"] = "WCW", ["nwo"] = "nWo", ["x360"] = "X360", ["xmen"] = "X-Men",
        ["yugioh"] = "Yu-Gi-Oh!", ["pokemon"] = "Pokémon", ["initiald"] = "Initial D", ["naomi2"] = "NAOMI 2",
        ["gbarecomp"] = "GBARecomp", ["psxrecomp"] = "PSXRecomp", ["rexglue"] = "ReXGlue", ["einhander"] = "Einhänder",
        ["dantes"] = "Dante's", ["soulcalibur"] = "SoulCalibur", ["co-op"] = "Co-op",
    };
    private static readonly HashSet<string> SmallTagWords = ["a", "an", "and", "in", "of", "on", "the", "to", "vs"];

    /// <summary>"harbour masters" → "Harbour Masters", "n64" → "N64", like the website's tagLabel.</summary>
    public static string TagLabel(string tag)
    {
        if (tag != tag.ToLowerInvariant()) return tag;
        return string.Join(' ', tag.Split(' ').Select((part, i) =>
            SpecialTagWords.TryGetValue(part, out var special) ? special
            : i > 0 && SmallTagWords.Contains(part) ? part
            : string.Join('-', part.Split('-').Select(piece => piece.Length == 0 ? piece : char.ToUpperInvariant(piece[0]) + piece[1..]))));
    }

    /// <summary>Best first by what players said, as the website and Quiver Launcher 4 order a game's ways to play.</summary>
    public static int ByPlayerFeedback(QuiverCatalogApp a, QuiverCatalogApp b)
    {
        static int Total(QuiverCatalogApp app) => app.Recommended + app.ReportIssues + app.ReportBroken;
        static double Score(QuiverCatalogApp app) => (app.Recommended + app.ReportIssues / 2.0 + 1) / (Total(app) + 2);
        var order = Score(b).CompareTo(Score(a));
        if (order == 0) order = Total(b).CompareTo(Total(a));
        if (order == 0) order = (b.LastReleaseAt ?? 0).CompareTo(a.LastReleaseAt ?? 0);
        return order != 0 ? order : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Whether every word of a search is in a game's title, so search can lead to the game.</summary>
    public static bool TitleMatches(string title, string search)
    {
        // Case, accents and punctuation aside: "pokemon" finds "Pokémon Red".
        static string Fold(string text) => new(text.Normalize(System.Text.NormalizationForm.FormD).ToLowerInvariant()
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        var words = Fold(search).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var folded = " " + Fold(title).Replace(" ", "") + " " + Fold(title) + " ";
        return words.Length > 0 && words.All(word => folded.Contains(word, StringComparison.Ordinal));
    }

    public static string ReleaseAge(double? releasedAt, DateTimeOffset now)
    {
        if (releasedAt is not { } at) return "No releases";
        var age = now - DateTimeOffset.FromUnixTimeMilliseconds((long)at);
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
