using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class ShellFeatureBindingsTests
{
    [AvaloniaFact]
    public async Task Editor_visibility_stays_bound_to_shell_after_feature_data_context_changes()
    {
        var view = new MainView(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        try
        {
            window.Show();
            var entry = view.FindControl<AppEntryEditorView>("EntryFormOverlay")!;
            var tags = view.FindControl<MetadataEditorView>("TagEditOverlay")!;
            var details = view.FindControl<CatalogDetailsView>("CatalogReviewDetailsPanel")!;
            Dispatcher.UIThread.RunJobs();
            entry.IsVisible.Should().BeFalse();
            tags.IsVisible.Should().BeFalse();
            details.IsVisible.Should().BeFalse();
            view.Shell.EntryEditorOpen = true;
            Dispatcher.UIThread.RunJobs();
            entry.IsVisible.Should().BeTrue();
            tags.IsVisible.Should().BeFalse();
            view.Shell.TagEditorOpen = true;
            details.DataContext = new CatalogSyncRowItem();
            view.Shell.CatalogDetailsOpen = true;
            Dispatcher.UIThread.RunJobs();
            tags.IsVisible.Should().BeTrue();
            details.IsVisible.Should().BeTrue();
            view.Shell.EntryEditorOpen = view.Shell.TagEditorOpen = view.Shell.CatalogDetailsOpen = false;
            Dispatcher.UIThread.RunJobs();
            entry.IsVisible.Should().BeFalse();
            tags.IsVisible.Should().BeFalse();
            details.IsVisible.Should().BeFalse();
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, AppsPath = Path.Combine(Path.GetTempPath(), "quiver-feature-tests", Guid.NewGuid().ToString("N")), EnableGamepadInput = false };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }

    [AvaloniaFact]
    public async Task Composed_features_accept_zone_transitions_and_empty_list_fallbacks()
    {
        var view = new MainView(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        var host = (IFeatureNavigationHost)view;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            host.ApplyTransition(new(GamepadNavigationZone.Library, 0)).Should().BeTrue();
            host.Navigation.ActiveZone.Should().Be(GamepadNavigationZone.Library);
            view.Shell.Mode = MainViewMode.AppCatalog;
            ((IModsFeatureHost)view).RefreshShell();
            host.ApplyTransition(new(GamepadNavigationZone.CatalogSources, 0)).Should().BeTrue();
            host.Navigation.ActiveZone.Should().Be(GamepadNavigationZone.CatalogSourcesToolbar);
            view.Shell.Mode = MainViewMode.Library;
            view.Shell.AppUpdatesOpen = true;
            ((IModsFeatureHost)view).RefreshShell();
            host.ApplyTransition(new(GamepadNavigationZone.AppUpdatesReviewList, 0)).Should().BeTrue();
            host.Navigation.ActiveZone.Should().Be(GamepadNavigationZone.AppUpdatesReviewToolbar);
            view.Shell.AppUpdatesOpen = false;
            view.Shell.ModsOpen = true;
            ((IModsFeatureHost)view).RefreshShell();
            host.ApplyTransition(new(GamepadNavigationZone.ModsOverlayList, 0)).Should().BeTrue();
            host.Navigation.ActiveZone.Should().Be(GamepadNavigationZone.ModsOverlayFilters);
            host.ApplyTransition(new(GamepadNavigationZone.Sidebar, 0)).Should().BeTrue();
            host.Navigation.ActiveZone.Should().Be(GamepadNavigationZone.Sidebar);
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public async Task Shell_shows_extracted_update_review_and_returns_to_catalog_sources()
    {
        var view = new MainView(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        try
        {
            view.Shell.AppUpdatesOpen = true;
            ((IModsFeatureHost)view).RefreshShell();
            view.FindControl<AppUpdateReviewView>("AppUpdatesReviewPanel")!.IsVisible.Should().BeTrue();
            view.Shell.AppUpdatesOpen = false;
            view.Shell.Mode = MainViewMode.AppCatalog;
            view.Shell.CatalogSubView = AppCatalogSubView.Sources;
            ((IModsFeatureHost)view).RefreshShell();
            view.FindControl<AppUpdateReviewView>("AppUpdatesReviewPanel")!.IsVisible.Should().BeFalse();
            view.FindControl<CatalogSourcesView>("CatalogSourcesPanel")!.IsVisible.Should().BeTrue();
            view.FindControl<CatalogReviewView>("CatalogReviewPanel")!.IsVisible.Should().BeFalse();
        }
        finally { await view.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public async Task Library_template_observes_settings_model_after_settings_controls_move()
    {
        var view = new MainView(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            var libraryView = view.FindControl<LibraryView>("LibraryPanel")!;
            var template = (IDataTemplate)libraryView.Resources["ListViewTemplate"]!;
            var card = template.Build(new GameInfo { Name = "Example", Repository = "example/app" })!;
            libraryView.FindControl<Grid>("Surface")!.Children.Add(card);
            window.Show();
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            Dispatcher.UIThread.RunJobs();
            var border = card as Border ?? card.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("gamecard"));
            border.Height.Should().Be(view.SettingsModel.ListRowHeight);
            view.SettingsModel.ListRowHeight = 232;
            Dispatcher.UIThread.RunJobs();
            border.Height.Should().Be(232);
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }
}
