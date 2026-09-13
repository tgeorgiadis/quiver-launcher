namespace QuiverLauncher.ViewModels;

public sealed record DocumentContent(string Text, string? ImageBaseUrl = null, bool IsMarkdown = true);

/// <summary>Owns one document request and ignores results from closed or superseded requests.</summary>
public sealed class DocumentViewModel : ObservableViewModel, IDisposable
{
    private CancellationTokenSource? _request;
    private string _title = "Changelog";
    private DocumentContent _content = new("", IsMarkdown: false);
    public string Title { get => _title; private set => Set(ref _title, value); }
    public DocumentContent Content { get => _content; private set => Set(ref _content, value); }

    public async Task OpenAsync(string title, string loadingMessage,
        Func<CancellationToken, Task<DocumentContent>> load, CancellationToken lifetime)
    {
        Cancel();
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _request = request;
        Title = title;
        Content = new DocumentContent(loadingMessage, IsMarkdown: false);
        try
        {
            var content = await load(request.Token);
            if (!request.IsCancellationRequested && ReferenceEquals(_request, request)) Content = content;
        }
        catch (Exception) when (request.IsCancellationRequested || !ReferenceEquals(_request, request)) { }
        finally
        {
            if (ReferenceEquals(_request, request)) _request = null;
            request.Dispose();
        }
    }

    public void Cancel()
    {
        _request?.Cancel();
        _request = null;
    }
    public void Dispose() => Cancel();
}
