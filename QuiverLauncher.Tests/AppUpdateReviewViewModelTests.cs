using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class AppUpdateReviewViewModelTests
{
    private sealed class Actions : IAppUpdateReviewActions
    {
        public bool IsReviewOpen { get; set; } = true;
        public IReadOnlyList<GameInfo> GetReviewRows() => [];
        public IReadOnlyList<GameInfo> GetPendingUpdates() => [];
        public Task UpdateAsync(GameInfo game, bool automaticSelection) => Task.CompletedTask;
        public Task SkipAsync(GameInfo game) => Task.CompletedTask;
        public void ShowVersions(GameInfo game) { }
        public void UpdatesChanged() { }
        public void ReturnToLibrary() => IsReviewOpen = false;
    }

    [Fact]
    public async Task Bulk_skip_returns_to_library_and_resets_busy_state()
    {
        var actions = new Actions();
        var model = new AppUpdateReviewViewModel();
        model.Configure(actions);
        await model.SkipAllAsync();
        actions.IsReviewOpen.Should().BeFalse();
        model.IsBusy.Should().BeFalse();
    }
}
