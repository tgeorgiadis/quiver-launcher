using System.Collections.ObjectModel;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public sealed class LibraryFiltersViewModel(GameManager manager, LibraryViewModel library)
{
    public AppSettings Settings => library.Settings.Current;
    public ObservableCollection<TagDisplayFilterListItem> TagDisplayFilters => library.TagDisplayFilters;
    public void Save() => library.Settings.SaveCurrent();
    public void SetScope(AppListScope scope)
    {
        manager.SetListScope(scope, Settings);
        library.Settings.Refresh();
        library.RefreshContinue();
        library.ApplySorting();
    }
    public void Toggle(string filterId)
    {
        Settings.ActiveTagDisplayFilterId = string.Equals(Settings.ActiveTagDisplayFilterId, filterId, StringComparison.OrdinalIgnoreCase) ? null : filterId;
        Save();
        manager.ApplyTagDisplayFilter(Settings);
        library.RefreshFilters();
        library.RefreshContinue();
        library.ApplySorting();
    }
    public void Delete(string filterId)
    {
        var filter = Settings.TagDisplayFilters.FirstOrDefault(f => f.Id == filterId);
        if (filter == null) return;
        Settings.TagDisplayFilters.Remove(filter);
        if (string.Equals(Settings.ActiveTagDisplayFilterId, filterId, StringComparison.OrdinalIgnoreCase)) Settings.ActiveTagDisplayFilterId = null;
        Save();
        library.RefreshFilters();
        manager.ApplyTagDisplayFilter(Settings);
        library.ApplySorting();
    }
    public int IndexOf(string? filterId) => filterId == null ? -1 : Settings.TagDisplayFilters.FindIndex(f => string.Equals(f.Id, filterId, StringComparison.OrdinalIgnoreCase));
    public void PreviewMove(string filterId, int targetIndex)
    {
        Settings.EnsureInitialized();
        var fromIndex = IndexOf(filterId);
        if (fromIndex < 0 || fromIndex == targetIndex) return;
        TagDisplayFilterReorder.Move(Settings.TagDisplayFilters, fromIndex, targetIndex);
        var uiIndex = TagDisplayFilters.Select((item, index) => (item, index)).FirstOrDefault(x => string.Equals(x.item.Id, filterId, StringComparison.OrdinalIgnoreCase), (null!, -1)).index;
        if (uiIndex >= 0 && targetIndex >= 0 && targetIndex < TagDisplayFilters.Count) TagDisplayFilters.Move(uiIndex, targetIndex);
    }
}
