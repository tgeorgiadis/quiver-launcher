using QuiverLauncher.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.ViewModels;

public sealed record AppEntryDraft(string Name, string Repository, string RepositorySource, string FolderName,
    string IconUrl, string Project, string CustomDisplayName, string Tags, string FilesToAdd,
    string ReleaseAssetFilter, string ModsPath, string ModsSources, bool ModsFolderPerMod, bool ManuallyManaged,
    string? EditingIdentityKey, string? EditingRepository);

public sealed record EntryNotice(string Message, string Title);

public sealed class AppEntryEditorViewModel : ObservableViewModel
{
    private IAppEntryService? _service;
    private GameInfo? _editing;
    private int _generation;
    private bool _showValidation;
    private bool _isBusy;
    public event Action<EntryNotice>? Notice;
    public event Action<GameInfo>? OpenFolderRequested;
    public string Title => _editing == null ? "Create New Entry" : "Edit App Entry";
    public bool IsCreating => _editing == null;
    public string SaveLabel => _editing == null ? "Create Entry" : "Update Entry";
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); Notify(nameof(CanSave)); } }
    public bool CanSave => !IsBusy;
    public bool ShowRepository => !ManuallyManaged;
    public bool HasValidation => !string.IsNullOrEmpty(Validation);
    public string Validation
    {
        get
        {
            if (!_showValidation) return string.Empty;
            if (string.IsNullOrWhiteSpace(Name)) return "Error: App name is required";
            if (!ManuallyManaged && string.IsNullOrWhiteSpace(Repository)) return "Error: Repository is required";
            if (string.IsNullOrWhiteSpace(FolderName)) return "Error: Folder name is required";
            if (!ManuallyManaged && (!Uri.TryCreate(Repository.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
                return "Warning: Repository should be a valid URL";
            if (!IsValidFolderName(FolderName.Trim())) return "Warning: Folder name contains invalid characters";
            return "All fields are valid";
        }
    }

    public void Configure(IAppEntryService service) => _service = service;
    public void Open(GameInfo? game = null)
    {
        ++_generation;
        _editing = game;
        _showValidation = false;
        Name = game?.Name ?? string.Empty;
        ManuallyManaged = game?.IsManuallyManaged == true;
        RepositorySource = game?.EffectiveRepositorySource ?? RepositorySourceIds.GitHub;
        Repository = game?.Repository ?? string.Empty;
        FolderName = game?.FolderName ?? string.Empty;
        IconUrl = game?.GameIconUrl ?? string.Empty;
        Project = game?.Project ?? string.Empty;
        CustomDisplayName = game?.CustomDisplayName ?? string.Empty;
        Tags = TagHelper.FormatTagsForDisplay(game?.Tags ?? []);
        FilesToAdd = AppFilesToAddService.FormatForDisplay(game?.FilesToAdd);
        ReleaseAssetFilter = game?.ReleaseAssetFilter ?? string.Empty;
        ModsPath = GameModsConfig.NormalizePath(game?.ModsPath);
        ModsSources = GameModsFormHelper.FormatSourcesForEditor(game?.ModsSources);
        ModsFolderPerMod = GameModsConfig.IsFolderPerMod(game?.ModsLayout);
        IsBusy = false;
        Notify(null);
    }
    public void Close() { ++_generation; _editing = null; _showValidation = false; IsBusy = false; }
    public AppEntryDraft CaptureDraft() => new(Name.Trim(), Repository.Trim(), RepositorySource, FolderName.Trim(),
        IconUrl.Trim(), Project.Trim(), CustomDisplayName.Trim(), Tags, FilesToAdd, ReleaseAssetFilter, ModsPath,
        ModsSources, ModsFolderPerMod, ManuallyManaged, _editing?.InstanceKey, _editing?.Repository);

    public async Task<bool> SaveAsync(CancellationToken token)
    {
        if (IsBusy || _service == null) return false;
        var generation = _generation;
        _showValidation = true;
        Notify(nameof(Validation));
        Notify(nameof(HasValidation));
        var draft = CaptureDraft();
        if (string.IsNullOrEmpty(draft.Name) || string.IsNullOrEmpty(draft.FolderName) ||
            (!draft.ManuallyManaged && string.IsNullOrEmpty(draft.Repository)))
        {
            Notice?.Invoke(new EntryNotice(draft.ManuallyManaged
                ? "Please fill in all required fields (Name, Folder Name)"
                : "Please fill in all required fields (Name, Repository, Folder Name)", "Validation Error"));
            return false;
        }
        IsBusy = true;
        try
        {
            var saved = await _service.SaveAsync(draft,
                notice => { if (!token.IsCancellationRequested && generation == _generation) Notice?.Invoke(notice); },
                game => { if (!token.IsCancellationRequested && generation == _generation) OpenFolderRequested?.Invoke(game); }, token);
            return saved && !token.IsCancellationRequested && generation == _generation;
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && generation == _generation)
                Notice?.Invoke(new EntryNotice($"Error saving app entry: {ex.Message}", "Error"));
            return false;
        }
        finally { if (generation == _generation) IsBusy = false; }
    }
    private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name)) return;
        Notify(nameof(Validation));
        Notify(nameof(HasValidation));
        Notify(nameof(ShowRepository));
    }
    private static bool IsValidFolderName(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        return !reserved.Contains(folderName.ToUpperInvariant());
    }

    private string _name = string.Empty;
    public string Name { get => _name; set => SetField(ref _name, value); }

    private string _repository = string.Empty;
    public string Repository { get => _repository; set => SetField(ref _repository, value); }

    private string _repositorySource = string.Empty;
    public string RepositorySource { get => _repositorySource; set => SetField(ref _repositorySource, value); }

    private string _folderName = string.Empty;
    public string FolderName { get => _folderName; set => SetField(ref _folderName, value); }

    private string _iconUrl = string.Empty;
    public string IconUrl { get => _iconUrl; set => SetField(ref _iconUrl, value); }

    private string _project = string.Empty;
    public string Project { get => _project; set => SetField(ref _project, value); }

    private string _customDisplayName = string.Empty;
    public string CustomDisplayName { get => _customDisplayName; set => SetField(ref _customDisplayName, value); }

    private string _tags = string.Empty;
    public string Tags { get => _tags; set => SetField(ref _tags, value); }

    private string _filesToAdd = string.Empty;
    public string FilesToAdd { get => _filesToAdd; set => SetField(ref _filesToAdd, value); }

    private string _releaseAssetFilter = string.Empty;
    public string ReleaseAssetFilter { get => _releaseAssetFilter; set => SetField(ref _releaseAssetFilter, value); }

    private string _modsPath = string.Empty;
    public string ModsPath { get => _modsPath; set => SetField(ref _modsPath, value); }

    private string _modsSources = string.Empty;
    public string ModsSources { get => _modsSources; set => SetField(ref _modsSources, value); }

    private bool _manuallyManaged;
    public bool ManuallyManaged { get => _manuallyManaged; set => SetField(ref _manuallyManaged, value); }

    private bool _modsFolderPerMod;
    public bool ModsFolderPerMod { get => _modsFolderPerMod; set => SetField(ref _modsFolderPerMod, value); }
}
