using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public sealed class CatalogDetailsViewModel : ObservableViewModel, IDisposable
{
    private CatalogSyncRowItem? _row;
    private string? _readmeKey;
    private bool _expanded;
    private bool _loading;
    private int _generation;
    public CatalogSyncRowItem? Row { get => _row; private set => Set(ref _row, value); }
    public bool Expanded { get => _expanded; set => Set(ref _expanded, value); }
    public bool IsLoading { get => _loading; private set => Set(ref _loading, value); }
    public DocumentViewModel Document { get; } = new();

    public async Task BindAsync(CatalogSyncRowItem row,
        Func<CatalogSyncRowItem, CancellationToken, Task<DocumentContent>> load, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Row = row;
        var key = $"{row.IdentityKey}|{row.Repository}";
        if (string.Equals(_readmeKey, key, StringComparison.OrdinalIgnoreCase)) return;
        _readmeKey = key;
        var generation = ++_generation;
        IsLoading = true;
        try
        {
            await Document.OpenAsync(row.TitleName, "", async cancellation =>
            {
                try { return await load(row, cancellation); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception ex) { return new DocumentContent($"Failed to load README: {ex.Message}", IsMarkdown: false); }
            }, token);
        }
        finally { if (generation == _generation && !token.IsCancellationRequested) IsLoading = false; }
    }

    public void Close()
    {
        ++_generation;
        Document.Cancel();
        Row = null;
        _readmeKey = null;
        Expanded = false;
        IsLoading = false;
    }
    public void Dispose() => Close();
}
