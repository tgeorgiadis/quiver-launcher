using System.Collections.ObjectModel;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;

namespace QuiverLauncher.ViewModels;

public interface IAppUpdateReviewActions
{
    bool IsReviewOpen { get; }
    IReadOnlyList<GameInfo> GetReviewRows();
    IReadOnlyList<GameInfo> GetPendingUpdates();
    Task UpdateAsync(GameInfo game, bool automaticSelection);
    Task SkipAsync(GameInfo game);
    void ShowVersions(GameInfo game);
    void UpdatesChanged();
    void ReturnToLibrary();
}

public sealed class AppUpdateReviewViewModel : ObservableViewModel
{
    private IAppUpdateReviewActions? _actions;
    private bool _isBusy;
    public ObservableCollection<GameInfo> Rows { get; } = [];
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) Notify(nameof(CanApply)); } }
    public bool IsEmpty => Rows.Count == 0;
    public bool CanApply => !IsBusy && Rows.Any(g => g.Status == GameStatus.UpdateAvailable);
    public string Header
    {
        get
        {
            var available = Rows.Count(g => g.Status == GameStatus.UpdateAvailable);
            var progress = Rows.Count - available;
            return Rows.Count == 0 ? "No app updates pending."
                : progress > 0 && available == 0 ? progress == 1 ? "1 app update in progress" : $"{progress} app updates in progress"
                : progress > 0 ? available == 1 ? $"1 app update available ({progress} in progress)" : $"{available} app updates available ({progress} in progress)"
                : available == 1 ? "1 app update is available" : $"{available} app updates are available";
        }
    }

    public void Configure(IAppUpdateReviewActions actions) => _actions = actions;
    public void Refresh()
    {
        if (_actions == null) return;
        Rows.Clear();
        foreach (var game in _actions.GetReviewRows()) Rows.Add(game);
        Notify(nameof(Header));
        Notify(nameof(IsEmpty));
        Notify(nameof(CanApply));
    }

    public async Task UpdateAsync(GameInfo game)
    {
        if (_actions == null) return;
        await _actions.UpdateAsync(game, false);
        RefreshAfterAction();
    }

    public async Task SkipAsync(GameInfo game)
    {
        if (_actions == null) return;
        await _actions.SkipAsync(game);
        RefreshAfterAction();
    }

    public void ShowVersions(GameInfo game) => _actions?.ShowVersions(game);
    public void Back() => _actions?.ReturnToLibrary();

    public async Task UpdateAllAsync()
    {
        if (_actions == null || IsBusy) return;
        IsBusy = true;
        try
        {
            foreach (var game in _actions.GetPendingUpdates().ToArray())
            {
                if (!_actions.IsReviewOpen) break;
                if (game.Status != GameStatus.UpdateAvailable) continue;
                await _actions.UpdateAsync(game, true);
                RefreshAfterAction();
            }
        }
        finally { IsBusy = false; }
    }

    public async Task SkipAllAsync()
    {
        if (_actions == null || IsBusy) return;
        IsBusy = true;
        try
        {
            foreach (var game in _actions.GetPendingUpdates().ToArray())
                await _actions.SkipAsync(game);
            RefreshAfterAction();
            Back();
        }
        finally { IsBusy = false; }
    }

    private void RefreshAfterAction()
    {
        _actions?.UpdatesChanged();
        Refresh();
        if (IsEmpty) Back();
    }
}
