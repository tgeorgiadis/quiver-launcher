using FluentAssertions;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class DocumentViewModelTests
{
    [Fact]
    public async Task Older_request_cannot_overwrite_new_document()
    {
        using var model = new DocumentViewModel();
        var old = new TaskCompletionSource<DocumentContent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = model.OpenAsync("Old", "Loading", _ => old.Task, TestContext.Current.CancellationToken);
        await model.OpenAsync("New", "Loading", _ => Task.FromResult(new DocumentContent("new body")), TestContext.Current.CancellationToken);
        old.SetResult(new DocumentContent("old body"));
        await first;
        model.Title.Should().Be("New");
        model.Content.Text.Should().Be("new body");
    }

    [Fact]
    public async Task Closing_cancels_load_and_ignores_its_late_completion()
    {
        using var model = new DocumentViewModel();
        var result = new TaskCompletionSource<DocumentContent>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        var loading = model.OpenAsync("Document", "Loading", token => { received = token; return result.Task; }, TestContext.Current.CancellationToken);
        model.Cancel();
        received.IsCancellationRequested.Should().BeTrue();
        result.SetResult(new DocumentContent("late body"));
        await loading;
        model.Content.Text.Should().Be("Loading");
    }
}
