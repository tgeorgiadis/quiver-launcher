using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public sealed class DisplayFilterEditorViewModel(SettingsViewModel settings) : ObservableViewModel
{
    private string? _editingId;
    private bool _open;
    private string _name = "", _tags = "", _excludeTags = "";
    private int _matchMode, _excludeMatchMode;
    public string Title => _editingId == null ? "Add Display Filter" : "Edit Display Filter";
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Tags { get => _tags; set => Set(ref _tags, value); }
    public string ExcludeTags { get => _excludeTags; set => Set(ref _excludeTags, value); }
    public int MatchModeIndex { get => _matchMode; set { if (Set(ref _matchMode, value)) Notify(nameof(MatchModeHelp)); } }
    public int ExcludeMatchModeIndex { get => _excludeMatchMode; set { if (Set(ref _excludeMatchMode, value)) Notify(nameof(ExcludeMatchModeHelp)); } }
    public string MatchModeHelp => MatchModeIndex == 1
        ? "Show apps that have every include tag listed. Exclude rules are applied next."
        : "Show apps that have at least one include tag listed. Exclude rules are applied next.";
    public string ExcludeMatchModeHelp => ExcludeMatchModeIndex == 1
        ? "Hide apps that have every exclude tag listed."
        : "Hide apps that have at least one of these tags.";
    public bool Open(string? filterId)
    {
        var current = settings.Current;
        current.EnsureInitialized();
        var filter = current.TagDisplayFilters.FirstOrDefault(f => f.Id == filterId);
        if (filterId != null && filter == null) return false;
        _open = true;
        _editingId = filterId;
        Name = filter?.Name ?? "";
        Tags = TagHelper.FormatTagsForDisplay(filter?.Tags ?? []);
        ExcludeTags = TagHelper.FormatTagsForDisplay(filter?.ExcludeTags ?? []);
        MatchModeIndex = filter?.MatchMode == TagFilterMatchMode.All ? 1 : 0;
        ExcludeMatchModeIndex = filter?.ExcludeMatchMode == TagFilterMatchMode.All ? 1 : 0;
        Notify(null);
        return true;
    }
    public DisplayFilterSaveResult Save()
    {
        if (!_open) return new(false, false);
        var name = Name.Trim();
        var tags = TagHelper.ParseCommaSeparatedTags(Tags);
        var excludes = TagHelper.ParseCommaSeparatedTags(ExcludeTags);
        if (name.Length == 0) return new(false, false, "Please enter a filter name.", "Validation Error");
        if (tags.Count == 0 && excludes.Count == 0) return new(false, false, "Please enter at least one include or exclude tag.", "Validation Error");
        var current = settings.Current;
        current.EnsureInitialized();
        if (current.TagDisplayFilters.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !string.Equals(f.Id, _editingId, StringComparison.OrdinalIgnoreCase)))
            return new(false, false, "A filter with this name already exists.", "Duplicate Filter");
        var isEdit = _editingId != null;
        var filter = isEdit ? current.TagDisplayFilters.FirstOrDefault(f => f.Id == _editingId) : new TagDisplayFilter();
        if (filter == null) return new(false, true);
        var previous = (filter.Name, filter.Tags, filter.ExcludeTags, filter.MatchMode, filter.ExcludeMatchMode);
        filter.Name = name;
        filter.Tags = tags;
        filter.ExcludeTags = excludes;
        filter.MatchMode = MatchModeIndex == 1 ? TagFilterMatchMode.All : TagFilterMatchMode.Any;
        filter.ExcludeMatchMode = ExcludeMatchModeIndex == 1 ? TagFilterMatchMode.All : TagFilterMatchMode.Any;
        if (!isEdit) current.TagDisplayFilters.Add(filter);
        try { settings.SaveCurrent(); }
        catch
        {
            if (!isEdit) current.TagDisplayFilters.Remove(filter);
            else (filter.Name, filter.Tags, filter.ExcludeTags, filter.MatchMode, filter.ExcludeMatchMode) = previous;
            throw;
        }
        return new(true, isEdit);
    }
    public void Close()
    {
        _open = false;
        _editingId = null;
        Name = Tags = ExcludeTags = "";
        MatchModeIndex = ExcludeMatchModeIndex = 0;
    }
}

public sealed record DisplayFilterSaveResult(bool Saved, bool WasEdit, string? Error = null, string? ErrorTitle = null);
