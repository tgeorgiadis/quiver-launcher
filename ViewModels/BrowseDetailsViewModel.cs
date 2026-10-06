using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public sealed record BrowseReviewLine(string Heading, string Body)
{
    public bool HasBody => Body.Length > 0;
}

/// <summary>
/// An app's details on Browse: what the site knows about it, recent player reviews (read-only;
/// reviews are written on quiverlauncher.com) and its README.
/// </summary>
public sealed class BrowseDetailsViewModel(QuiverCatalogClient client, Func<QuiverCatalogApp, QuiverCatalogProject, GameInfo> toLibraryEntry,
    Func<GameInfo, CancellationToken, Task<DocumentContent>> repositoryReadme) : ObservableViewModel, IDisposable
{
    public const int ReviewCount = 10;
    private int _generation;
    private BrowseItem? _item;
    private GameInfo? _entry;
    private IReadOnlyList<BrowseReviewLine> _reviews = [];
    private string _reviewsStatus = "";
    private string _error = "";

    public BrowseItem? Item { get => _item; private set => Set(ref _item, value); }
    /// <summary>The app as a library entry; null until the catalog says where it comes from.</summary>
    public GameInfo? Entry { get => _entry; private set => Set(ref _entry, value); }
    public IReadOnlyList<BrowseReviewLine> Reviews { get => _reviews; private set => Set(ref _reviews, value); }
    public string ReviewsStatus { get => _reviewsStatus; private set => Set(ref _reviewsStatus, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public DocumentViewModel Readme { get; } = new();
    public string? ReviewUrl => Item?.App is { } app ? QuiverCatalogClient.ReviewPageUrl(app.Slug) : null;

    public async Task OpenAsync(BrowseItem item, CancellationToken token)
    {
        var generation = ++_generation;
        Item = item;
        Entry = item.ListApp;
        Error = "";
        Reviews = [];
        ReviewsStatus = item.App == null ? "" : "Loading reviews…";
        var readme = Readme.OpenAsync(item.Title, "Loading README…", ct => LoadReadmeAsync(item, ct), token);
        if (item.App is { } app)
            await Task.WhenAll(LoadDetailAsync(app, generation, token), LoadReviewsAsync(app, generation, token));
        await readme;
    }

    public void Close()
    {
        ++_generation;
        Readme.Cancel();
        Item = null;
        Entry = null;
        Reviews = [];
        ReviewsStatus = "";
        Error = "";
    }

    public void Dispose() => Close();

    private async Task LoadDetailAsync(QuiverCatalogApp app, int generation, CancellationToken token)
    {
        try
        {
            var detail = await client.GetDetailAsync(app.Slug, token);
            if (generation == _generation) Entry = toLibraryEntry(detail.Entry, detail.Project);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            if (generation == _generation) Error = $"Couldn't load this app from quiverlauncher.com, so it can't be added right now. {ex.Message}";
        }
    }

    private async Task LoadReviewsAsync(QuiverCatalogApp app, int generation, CancellationToken token)
    {
        try
        {
            var page = await client.GetReviewsAsync(app.Slug, ReviewCount, token);
            if (generation != _generation) return;
            Reviews = page.Items.Select(ToLine).ToList();
            ReviewsStatus = Reviews.Count == 0 ? "No one has said how it runs yet." : "";
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            if (generation == _generation) ReviewsStatus = $"Couldn't load reviews. {ex.Message}";
        }
    }

    private async Task<DocumentContent> LoadReadmeAsync(BrowseItem item, CancellationToken token)
    {
        try
        {
            if (item.App == null)
                return item.ListApp == null ? new("No README.", IsMarkdown: false) : await repositoryReadme(item.ListApp, token);
            var readme = await client.GetReadmeAsync(item.App.Slug, token);
            return string.IsNullOrWhiteSpace(readme?.Markdown)
                ? new(string.IsNullOrWhiteSpace(item.App.Description) ? "This app has no README." : item.App.Description, IsMarkdown: false)
                : new(readme.Markdown, readme.RawBase);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            return new($"Couldn't load the README. {ex.Message}", IsMarkdown: false);
        }
    }

    internal static BrowseReviewLine ToLine(QuiverCatalogReview review)
    {
        var parts = new List<string> { string.IsNullOrWhiteSpace(review.Author) ? "A player" : review.Author.Trim(), BrowseText.ReviewResult(review.Result) };
        if (review.Platform is { Length: > 0 } platform) parts.Add(BrowseText.PlatformNames([platform]));
        if (review.Version is { Length: > 0 } version) parts.Add($"tested on {version}");
        if (review.CreatedAt > 0) parts.Add(DateTimeOffset.FromUnixTimeMilliseconds((long)review.CreatedAt).LocalDateTime.ToString("d"));
        return new(string.Join(" · ", parts.Where(p => p.Length > 0)), review.Body.Trim());
    }
}
