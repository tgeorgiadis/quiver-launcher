using QuiverLauncher.Core.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

public sealed class CatalogReadmeService(GameManager manager, SettingsViewModel settings)
{
    private readonly RepositoryReadmeService _reader = new();
    public async Task<DocumentContent> LoadAsync(CatalogSyncRowItem row, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(row.Repository)) return new("No repository README.", IsMarkdown: false);
        var result = await _reader.GetReadmeAsync(manager.HttpClient, row.EffectiveRepositorySource, row.Repository,
            settings.Current.GitHubApiToken, settings.Current.GitLabApiToken, QuiverLauncherPaths.CacheDirectory, DateTime.UtcNow, token);
        return result.Status switch
        {
            RepositoryReadmeStatus.Markdown when !string.IsNullOrWhiteSpace(result.Markdown) => new(result.Markdown, result.RawRootUrl),
            RepositoryReadmeStatus.NoRepository => new("No repository README.", IsMarkdown: false),
            RepositoryReadmeStatus.Error => new(result.ErrorMessage ?? "Failed to load README.", IsMarkdown: false),
            _ => new("No README found for this repository.", IsMarkdown: false),
        };
    }
}
