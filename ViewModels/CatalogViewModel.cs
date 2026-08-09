using System.Collections.ObjectModel;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public class CatalogViewModel
{
    public CatalogSourceListFilter SourceListFilter { get; set; } = CatalogSourceListFilter.Enabled;

    public IReadOnlyList<CatalogSourceListItem> BuildSourceListItems(
        AppSettings settings,
        CatalogSourceListFilter? filter = null)
    {
        settings.EnsureInitialized();
        var activeFilter = filter ?? SourceListFilter;

        return settings.AppCatalogSources
            .Where(s => activeFilter switch
            {
                CatalogSourceListFilter.Enabled => s.Enabled,
                CatalogSourceListFilter.Disabled => !s.Enabled,
                _ => true,
            })
            .OrderByDescending(s => s.Enabled)
            .ThenByDescending(s => s.PendingReviewCount > 0)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CatalogSourceListItem.FromSource)
            .ToList();
    }

    public void RefreshSourceList(ObservableCollection<CatalogSourceListItem> target, AppSettings settings)
    {
        var items = BuildSourceListItems(settings);
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    public AppCatalogSource CreateSource(string location) =>
        new()
        {
            Name = "",
            Location = location,
            Enabled = true,
            LastFetchedUtc = DateTime.UtcNow,
            LastError = null,
        };
}
