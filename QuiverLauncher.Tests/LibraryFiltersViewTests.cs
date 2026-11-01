using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class LibraryFiltersViewTests
{
    [AvaloniaFact]
    public async Task Sidebar_sorts_saves_and_offers_one_add_filter_control()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        using var library = new LibraryViewModel(manager, new SettingsViewModel(store));
        await using var session = new LauncherSession();
        var view = new LibraryFiltersView();
        var saves = 0;
        view.Configure(new LibraryFiltersViewModel(manager, library), session, (_, _) => Task.CompletedTask, () => saves++);
        var window = new Window { Content = view, Width = 260, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var sort = view.FindControl<ComboBox>("SortByComboBox")!;
            var add = view.FindControl<Button>("AddDisplayFilterButton")!;

            view.SelectSort("LastPlayed");
            library.SortBy.Should().Be("LastPlayed");
            store.Current.SortBy.Should().Be("LastPlayed");
            saves.Should().Be(1);

            var controls = view.CollectNavigationControls();
            controls[0].Should().BeSameAs(sort);
            controls.Should().Contain(add);
            view.GetVisualDescendants().OfType<Button>().Should().NotContain(b => b.Content as string == "Add Filter");

            // Mobile keeps sorting in its header menu, so the drawer leaves the dropdown out.
            view.ApplyMobileLayout();
            Dispatcher.UIThread.RunJobs();
            view.CollectNavigationControls().Should().NotContain(sort);
        }
        finally { window.Close(); }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, AppsPath = Path.Combine(Path.GetTempPath(), "quiver-filters-view", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
