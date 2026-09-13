using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogDetailsViewModelTests
{
    [Fact]
    public async Task Reopening_same_repository_does_not_accept_old_readme_or_clear_loading()
    {
        var model = new CatalogDetailsViewModel();
        var old = new TaskCompletionSource<DocumentContent>();
        var latest = new TaskCompletionSource<DocumentContent>();
        var row = new CatalogSyncRowItem();
        var first = model.BindAsync(row, (_, _) => old.Task, CancellationToken.None);
        model.Close();
        var second = model.BindAsync(row, (_, _) => latest.Task, CancellationToken.None);
        old.SetResult(new("old"));
        await first;
        model.IsLoading.Should().BeTrue();
        model.Document.Content.Text.Should().NotBe("old");
        latest.SetResult(new("latest"));
        await second;
        model.Document.Content.Text.Should().Be("latest");
        model.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Closing_pending_details_cancels_and_ignores_failure()
    {
        var model = new CatalogDetailsViewModel();
        var pending = new TaskCompletionSource<DocumentContent>();
        var changes = 0;
        var task = model.BindAsync(new CatalogSyncRowItem(), (_, _) => pending.Task, CancellationToken.None);
        model.Close();
        model.PropertyChanged += (_, _) => changes++;
        pending.SetException(new IOException("late failure"));
        await task;
        model.Row.Should().BeNull();
        model.IsLoading.Should().BeFalse();
        changes.Should().Be(0);
    }
}
