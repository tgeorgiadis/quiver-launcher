using FluentAssertions;
using QuiverLauncher.Services.Mods;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class ModDetailsViewModelTests
{
    private static ModListItem Item(string id) => new()
    {
        Package = new() { ProviderId = "test", SourceKey = "source", Id = id, Owner = "author", Name = id, FullName = id }
    };

    [Fact]
    public async Task Replaced_mod_ignores_old_response_and_preserves_new_tab_cache()
    {
        var model = new ModDetailsViewModel();
        var old = new TaskCompletionSource<string?>();
        CancellationToken oldToken = default;
        var first = model.OpenAsync(Item("first"), (_, _, token) => { oldToken = token; return old.Task; }, CancellationToken.None);
        var calls = 0;
        await model.OpenAsync(Item("second"), (_, changelog, _) => { calls++; return Task.FromResult<string?>(changelog ? "changes" : "readme"); }, CancellationToken.None);
        oldToken.IsCancellationRequested.Should().BeTrue();
        old.SetResult("stale readme");
        await first;
        model.Content.Text.Should().Be("readme");
        await model.SelectTabAsync("Changelog");
        await model.SelectTabAsync("Details");
        model.Content.Text.Should().Be("readme");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Tab_switch_and_close_ignore_late_failures_and_settle_loading()
    {
        var model = new ModDetailsViewModel();
        var readme = new TaskCompletionSource<string?>();
        var changes = new TaskCompletionSource<string?>();
        var open = model.OpenAsync(Item("mod"), (_, changelog, _) => changelog ? changes.Task : readme.Task, CancellationToken.None);
        var tab = model.SelectTabAsync("Changelog");
        readme.SetResult("old tab");
        await open;
        model.Content.Text.Should().Be("Loading changelog…");
        model.Close();
        var notifications = 0;
        model.PropertyChanged += (_, _) => notifications++;
        changes.SetException(new IOException("late"));
        await tab;
        model.Item.Should().BeNull();
        model.IsLoading.Should().BeFalse();
        notifications.Should().Be(0);
    }

    [Fact]
    public async Task Failed_document_can_be_retried_and_empty_documents_are_cached()
    {
        var model = new ModDetailsViewModel();
        var calls = 0;
        await model.OpenAsync(Item("mod"), (_, _, _) => ++calls == 1 ? Task.FromException<string?>(new IOException("offline")) : Task.FromResult<string?>(null), CancellationToken.None);
        model.Content.Text.Should().Contain("offline");
        await model.SelectTabAsync("Details");
        model.Content.Text.Should().Be("No readme available.");
        await model.SelectTabAsync("Details");
        calls.Should().Be(2);
    }
}
