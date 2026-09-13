using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class LibraryToolbarTests
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, AppsPath = Path.Combine(Path.GetTempPath(), "quiver-toolbar", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }

    [AvaloniaFact]
    public async Task Toolbar_edits_search_sorts_and_requests_add_after_mobile_reparenting()
    {
        using var manager = new GameManager(new Store());
        using var model = new LibraryViewModel(manager, new SettingsViewModel(new Store()));
        await using var session = new LauncherSession();
        var toolbar = new LibraryToolbarView();
        var saves = 0;
        var adds = 0;
        toolbar.Configure(model, session, () => saves++);
        toolbar.AddRequested += () => adds++;
        var header = new Grid();
        var window = new Window { Content = new StackPanel { Children = { header, toolbar } } };
        try
        {
            window.Show();
            toolbar.MoveSearchTo(header);
            toolbar.NavigationControls(searchOnly: true).Should().BeEmpty();
            toolbar.SetSearchVisible(true);
            toolbar.NavigationControls(searchOnly: true).Should().ContainSingle().Which.Should().BeOfType<TextBox>();
            var changed = new TaskCompletionSource();
            toolbar.SearchChanged += () => changed.TrySetResult();
            toolbar.FindControl<TextBox>("LibrarySearchTextBox")!.Text = "example";
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.SearchText.Should().Be("example");
            toolbar.HasQuery.Should().BeTrue();
            toolbar.ClearSearch();
            Dispatcher.UIThread.RunJobs();
            model.SearchText.Should().BeEmpty();
            toolbar.SelectSort("NameDesc");
            model.SortBy.Should().Be("NameDesc");
            model.Settings.Current.SortBy.Should().Be("NameDesc");
            saves.Should().BeGreaterThan(0);
            toolbar.FindControl<Button>("AddNewEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            adds.Should().Be(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_session_cancels_toolbar_search_without_publishing_results()
    {
        using var manager = new GameManager(new Store());
        using var model = new LibraryViewModel(manager, new SettingsViewModel(new Store()));
        var session = new LauncherSession();
        var toolbar = new LibraryToolbarView();
        var changed = 0;
        toolbar.Configure(model, session, () => { });
        toolbar.SearchChanged += () => changed++;
        toolbar.FindControl<TextBox>("LibrarySearchTextBox")!.Text = "pending";
        Dispatcher.UIThread.RunJobs();
        await session.DisposeAsync();
        model.SearchText.Should().BeEmpty();
        changed.Should().Be(0);
    }
}
