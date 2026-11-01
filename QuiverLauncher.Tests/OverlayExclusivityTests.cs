using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class OverlayExclusivityTests
{
    [AvaloniaFact]
    public async Task Opening_settings_or_a_dialog_closes_whichever_one_was_open()
    {
        var view = new MainView(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var settings = view.FindControl<SettingsView>("SettingsPanel")!;
            var filter = view.FindControl<DisplayFilterEditorView>("DisplayFilterOverlay")!;
            var filters = view.FindControl<LibraryFiltersView>("LibraryFiltersPanel")!;
            var toolbar = view.FindControl<LibraryToolbarView>("LibraryToolbar")!;
            var showTags = typeof(MainView).GetMethod("ShowTagEditOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Click(Control root, string name)
            {
                root.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }
            void EditTags()
            {
                showTags.Invoke(view, [new GameInfo { Name = "App", Repository = "owner/app", FolderName = "app" }, MetadataEditMode.Tags]);
                Dispatcher.UIThread.RunJobs();
            }
            void OnlyOpen(string expected) => new (string Name, bool Open)[]
            {
                ("settings", view.Shell.SettingsOpen && settings.IsVisible),
                ("filter", filter.IsVisible),
                ("entry", view.Shell.EntryEditorOpen),
                ("tags", view.Shell.TagEditorOpen),
            }.Where(o => o.Open).Select(o => o.Name).Should().Equal(expected);

            // The reported case, both ways round.
            Click(view, "SettingsButton"); OnlyOpen("settings");
            Click(filters, "AddDisplayFilterButton"); OnlyOpen("filter");
            Click(view, "SettingsButton"); OnlyOpen("settings");
            // The other dialogs follow the same rule.
            Click(toolbar, "AddNewEntryButton"); OnlyOpen("entry");
            Click(filters, "AddDisplayFilterButton"); OnlyOpen("filter");
            EditTags(); OnlyOpen("tags");
            Click(toolbar, "AddNewEntryButton"); OnlyOpen("entry");
            EditTags(); OnlyOpen("tags");
            Click(view, "SettingsButton"); OnlyOpen("settings");

            // App and mod pages cover Settings, so opening one closes it.
            view.Shell.BrowseDetailsOpen = true;
            view.Shell.SettingsOpen.Should().BeFalse();
            settings.IsVisible.Should().BeFalse();
            view.Shell.BrowseDetailsOpen = false;
            Click(view, "SettingsButton"); OnlyOpen("settings");
            view.Shell.ModDetailsOpen = true;
            view.Shell.SettingsOpen.Should().BeFalse();
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public async Task Window_close_button_closes_the_filter_dialog_before_the_launcher()
    {
        var view = new MainView(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        using var host = new DesktopHostController(window, view);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var filter = view.FindControl<DisplayFilterEditorView>("DisplayFilterOverlay")!;
            void Click(Control root, string name)
            {
                root.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }

            Click(view.FindControl<LibraryFiltersView>("LibraryFiltersPanel")!, "AddDisplayFilterButton");
            filter.IsVisible.Should().BeTrue();
            Click(view, "CloseLauncherButton");
            filter.IsVisible.Should().BeFalse();
            window.IsVisible.Should().BeTrue();
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, AppsPath = Path.Combine(Path.GetTempPath(), "quiver-overlay-tests", Guid.NewGuid().ToString("N")), EnableGamepadInput = false };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
