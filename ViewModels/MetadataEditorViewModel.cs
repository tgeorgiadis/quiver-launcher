using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public enum MetadataEditMode { Tags, CustomDisplayName }

public sealed class MetadataEditorViewModel : ObservableViewModel
{
    private Func<GameInfo, MetadataEditMode, string, CancellationToken, Task>? _save;
    private GameInfo? _game;
    private int _generation;
    private MetadataEditMode _mode;
    private string _text = string.Empty;
    private bool _isBusy;
    public string Text { get => _text; set => Set(ref _text, value); }
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); Notify(nameof(CanSave)); } }
    public bool CanSave => _game != null && !IsBusy;
    public string AppName => _game?.DisplayName ?? string.Empty;
    public string Title => _mode == MetadataEditMode.Tags ? "Edit Tags" : "Custom Display Name";
    public string Placeholder => _mode == MetadataEditMode.Tags ? "Tags (comma-separated)" : "Leave blank to use library name style";

    public void Configure(Func<GameInfo, MetadataEditMode, string, CancellationToken, Task> save) => _save = save;
    public void Open(GameInfo game, MetadataEditMode mode)
    {
        ++_generation;
        _game = game;
        _mode = mode;
        Text = mode == MetadataEditMode.Tags ? TagHelper.FormatTagsForDisplay(game.Tags) : game.CustomDisplayName ?? string.Empty;
        IsBusy = false;
        Notify(null);
    }
    public void Close()
    {
        ++_generation;
        _game = null;
        IsBusy = false;
        Notify(null);
    }
    public async Task<bool> SaveAsync(CancellationToken token)
    {
        if (!CanSave || _save == null) return false;
        var generation = _generation;
        var game = _game!;
        var mode = _mode;
        var text = Text;
        IsBusy = true;
        try
        {
            await _save(game, mode, text, token);
            return !token.IsCancellationRequested && generation == _generation;
        }
        catch (Exception) when (token.IsCancellationRequested || generation != _generation) { return false; }
        finally { if (generation == _generation) IsBusy = false; }
    }
}
