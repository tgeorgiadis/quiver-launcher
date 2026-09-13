using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class LibraryViewModelTests
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, AppsPath = Path.Combine(Path.GetTempPath(), "quiver-library-tests", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    [Fact]
    public async Task New_search_cancels_older_debounce_and_clear_applies_immediately()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        using var model = new LibraryViewModel(manager, new SettingsViewModel(store));
        var old = model.SearchAsync("old", CancellationToken.None);
        (await model.SearchAsync("latest", CancellationToken.None, debounce: false)).Should().BeTrue();
        (await old).Should().BeFalse();
        model.SearchText.Should().Be("latest");
        (await model.SearchAsync("", CancellationToken.None)).Should().BeTrue();
        model.SearchText.Should().BeEmpty();
    }
    [Fact]
    public async Task Closing_library_cancels_search_and_repeated_close_is_safe()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        var model = new LibraryViewModel(manager, new SettingsViewModel(store));
        var pending = model.SearchAsync("pending", CancellationToken.None);
        model.Dispose();
        model.Dispose();
        (await pending).Should().BeFalse();
        (await model.SearchAsync("closed", CancellationToken.None, debounce: false)).Should().BeFalse();
        model.SearchText.Should().BeEmpty();
    }
}
