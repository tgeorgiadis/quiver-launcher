using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using QuiverLauncher.Core.Models;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Views;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class LibraryListRowTests
{
    [AvaloniaTheory]
    [InlineData("ListViewTemplate")]
    [InlineData("GridViewTemplate")]
    [InlineData("CompactGridViewTemplate")]
    public async Task Cached_version_hint_and_tooltip_bind_in_every_library_layout(string template)
    {
        var view = CreateView();
        var window = new Window { Content = view, Width = 1000, Height = 750 };
        try
        {
            view.SettingsModel.ListRowHeight = 120;
            var library = view.FindControl<LibraryView>("LibraryPanel")!;
            var game = new GameInfo { Name = "Cached app", Repository = "fixture/cached" };
            game.ApplyLastKnownVersion("v2.0");
            var card = ((IDataTemplate)library.Resources[template]!).Build(game)!;
            card.DataContext = game;
            library.FindControl<Grid>("Surface")!.Children.Add(card);
            window.Show(); Settle(window);
            var labels = card.GetVisualDescendants().OfType<HoverScrollText>()
                .Where(c => c.Text == "Latest: v2.0 (pending check)").ToArray();
            labels.Should().NotBeEmpty();
            foreach (var label in labels)
                ToolTip.GetTip(label)!.ToString().Should().Contain("Verification is pending");
            game.ApplyCachedRelease("v2.1", new() { tag_name = "v2.1" });
            Settle(window);
            foreach (var label in labels)
            {
                label.Text.Should().Be("Latest: v2.1");
                ToolTip.GetTip(label).Should().BeNull();
            }
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public void Legacy_settings_and_contextual_sliders_preserve_grid_size()
    {
        var store = new Store(); store.Current.UseGridView = false; store.Current.SlotSize = 180;
        var model = new SettingsViewModel(store); model.Load();
        model.ListRowHeight.Should().Be(180); store.Saves.Should().Be(0);
        var settings = new SettingsView { DataContext = model };
        var window = new Window { Content = settings };
        try
        {
            window.Show(); Settle(window);
            model.ApplyCardLayout(CardLayoutPreset.List);
            model.ListRowHeight.Should().Be(96); model.SlotSize.Should().Be(180);
            settings.FindControl<Slider>("ListRowHeightSlider")!.Value = 72; Settle(window);
            model.ListRowHeight.Should().Be(72);
            model.UseGridView = true; Settle(window); model.SlotSize.Should().Be(180);
            model.UseGridView = false; Settle(window); model.ListRowHeight.Should().Be(72);
            var roundtrip = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(store.Current))!;
            roundtrip.ListRowHeight.Should().Be(72); roundtrip.SlotSize.Should().Be(180);
            model.ListRowHeight = 1; model.ListRowHeight.Should().Be(72);
            model.ListRowHeight = 900; model.ListRowHeight.Should().Be(400);
        }
        finally { window.Close(); }
    }
    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static MainView CreateView() => new(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
    private sealed class Store : ISettingsStore
    {
        public int Saves;
        public AppSettings Current { get; } = new() { FirstStartup = false, UseGridView = false, AppsPath = Path.Combine(Path.GetTempPath(), "quiver-list-tests", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Saves++;
    }
}
