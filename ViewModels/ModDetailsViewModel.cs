using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.ViewModels;

/// <summary>Owns one mod's documentation, including tab caches and request cancellation.</summary>
public sealed class ModDetailsViewModel : ObservableViewModel, IDisposable
{
    private CancellationTokenSource? _request;
    private readonly Dictionary<string, string?> _documents = new(StringComparer.OrdinalIgnoreCase);
    private Func<ModPackage, bool, CancellationToken, Task<string?>>? _load;
    private CancellationToken _lifetime;
    private int _generation;
    private ModListItem? _item;
    private string _tab = "Details";
    private DocumentContent _content = new("", IsMarkdown: false);
    private bool _isLoading;
    public ModListItem? Item { get => _item; private set => Set(ref _item, value); }
    public string Title => Item is { } item ? $"{item.DisplayName} · v{item.LatestVersion}" : "Mod Details";
    public bool HasPage => !string.IsNullOrWhiteSpace(Item?.Package.PackagePageUrl);
    public string Tab { get => _tab; private set => Set(ref _tab, value); }
    public DocumentContent Content { get => _content; private set => Set(ref _content, value); }
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    public Task OpenAsync(ModListItem item, Func<ModPackage, bool, CancellationToken, Task<string?>> load, CancellationToken lifetime)
    {
        Close();
        if (lifetime.IsCancellationRequested) return Task.CompletedTask;
        _load = load;
        _lifetime = lifetime;
        Item = item;
        Notify(nameof(Title));
        Notify(nameof(HasPage));
        return SelectTabAsync("Details");
    }

    public async Task SelectTabAsync(string tab)
    {
        if (Item is not { } item || _lifetime.IsCancellationRequested) return;
        _request?.Cancel();
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _request = request;
        var generation = ++_generation;
        Tab = tab;
        var changelog = string.Equals(tab, "Changelog", StringComparison.OrdinalIgnoreCase);
        IsLoading = true;
        Content = new(changelog ? "Loading changelog…" : "Loading details…", IsMarkdown: false);
        bool Current() => generation == _generation && !request.IsCancellationRequested;
        try
        {
            if (!_documents.TryGetValue(tab, out var markdown))
                markdown = await _load!(item.Package, changelog, request.Token);
            if (!Current()) return;
            _documents[tab] = markdown;
            Content = string.IsNullOrWhiteSpace(markdown)
                ? new(changelog ? "No changelog available." : "No readme available.", IsMarkdown: false)
                : new(markdown);
        }
        catch (Exception) when (!Current()) { }
        catch (Exception ex)
        {
            Content = new(ex is NotSupportedException ? "This mod provider does not support documentation."
                : $"Failed to load documentation: {ex.Message}", IsMarkdown: false);
        }
        finally
        {
            if (Current()) IsLoading = false;
            if (ReferenceEquals(_request, request)) _request = null;
            request.Dispose();
        }
    }

    public void Close()
    {
        ++_generation;
        _request?.Cancel();
        _request = null;
        _documents.Clear();
        Item = null;
        IsLoading = false;
        Content = new("", IsMarkdown: false);
    }
    public void Dispose() => Close();
}
