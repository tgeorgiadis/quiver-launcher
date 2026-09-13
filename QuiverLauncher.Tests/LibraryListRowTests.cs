using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using QuiverLauncher.Core.Models;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    [InlineData(72)] [InlineData(96)] [InlineData(120)] [InlineData(180)]
    public async Task Rows_fit_metadata_actions_and_progress(int height)
    {
        var view = CreateView();
        var window = new Window { Content = view, Width = 750, Height = 600 };
        try
        {
            view.SettingsModel.ListRowHeight = height;
            view.SettingsModel.IconSize = 512;
            view.SettingsModel.ActionButtonSize = 16;
            var library = view.FindControl<LibraryView>("LibraryPanel")!;
            var game = new GameInfo { Name = "A very long app name which must not overlap the actions", Project = "A long project subtitle", Repository = "example/app", InstalledVersion = "1.0", LatestVersion = "2.0", PreferredVersion = "1.5", Status = GameStatus.Installed, HasPendingCatalogChanges = true, ShowLibraryUpdateBadges = true, Tags = new List<string> { "Platformer", "Adventure", "Community", "Another tag" } };
            game.RefreshLibraryCardTags();
            var card = (Border)((IDataTemplate)library.Resources["ListViewTemplate"]!).Build(game)!;
            card.DataContext = game;
            library.FindControl<Grid>("Surface")!.Children.Add(card);
            window.Show(); Settle(window);
            var panel = card.GetVisualDescendants().OfType<LibraryListRowLayout>().Single();
            Control Role(string role) => panel.Children.Single(c => LibraryListRowLayout.GetRole(c) == role);
            foreach (var status in new[] { GameStatus.Installed, GameStatus.NotInstalled, GameStatus.UpdateAvailable, GameStatus.Downloading, GameStatus.Installing })
            {
                game.Status = status; game.DownloadProgress = 45; Settle(window);
                card.Height.Should().Be(height);
                Role("Title").IsVisible.Should().BeTrue(); Role("Subtitle").IsVisible.Should().BeTrue(); Role("Status").IsVisible.Should().BeTrue();
                Role("Title").Bounds.Top.Should().Be(6);
                Role("Thumbnail").Bounds.Right.Should().BeLessThan(Role("Title").Bounds.Left);
                Role("Title").Bounds.Right.Should().BeLessThan(Role("Actions").Bounds.Left);
                foreach (var control in panel.Children.Where(c => c.IsVisible))
                {
                    control.Bounds.Left.Should().BeGreaterThanOrEqualTo(0, LibraryListRowLayout.GetRole(control));
                    control.Bounds.Right.Should().BeLessThanOrEqualTo(panel.Bounds.Width + 1, LibraryListRowLayout.GetRole(control));
                    control.Bounds.Bottom.Should().BeLessThanOrEqualTo(panel.Bounds.Height + 1, LibraryListRowLayout.GetRole(control));
                }
                Role("Progress").IsVisible.Should().Be(game.IsDownloading);
                Role("Badge").IsVisible.Should().BeTrue();
                if (height == 72)
                {
                    Role("Latest").IsVisible.Should().BeFalse(); Role("Preferred").IsVisible.Should().BeFalse(); Role("Tags").IsVisible.Should().BeFalse();
                }
                if (height == 96) Role("Latest").IsVisible.Should().BeTrue();
                if (height == 180) { Role("Tags").IsVisible.Should().BeTrue(); Role("Tags").Bounds.Top.Should().BeGreaterThanOrEqualTo(Role("Preferred").Bounds.Bottom + 5); }
                ToolTip.GetTip(panel)!.ToString().Should().Contain("2.0").And.Contain("platformer");
            }
            var options = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("options-listview"));
            options.Bounds.Width.Should().Be(32);
            options.GetVisualDescendants().OfType<Ellipse>().Should().HaveCount(3);
            foreach (var dot in options.GetVisualDescendants().OfType<Ellipse>())
            {
                var point = dot.TranslatePoint(default, options)!.Value;
                point.X.Should().BeGreaterThanOrEqualTo(0); (point.X + dot.Bounds.Width).Should().BeLessThanOrEqualTo(options.Bounds.Width);
            }
            game.Status = GameStatus.Installed; Settle(window);
            var position = options.TranslatePoint(new Rect(options.Bounds.Size).Center, window)!.Value;
            window.MouseDown(position, MouseButton.Left); window.MouseUp(position, MouseButton.Left); Settle(window);
            options.ContextMenu!.IsOpen.Should().BeTrue(); options.ContextMenu.Close();
            game.IsGamepadFocused = true;
            view.SettingsModel.ListRowHeight = 80; Settle(window);
            game.IsGamepadFocused.Should().BeTrue(); card.Height.Should().Be(80);
            game.LatestVersion = game.InstalledVersion; view.SettingsModel.ListRowHeight = 180; Settle(window);
            Role("Latest").IsVisible.Should().BeFalse();
            game.PreferredVersion = null;
            view.SettingsModel.ListRowHeight = 96; Settle(window);
            Role("Tags").IsVisible.Should().BeTrue();
            Role("Tags").Bounds.Top.Should().BeGreaterThanOrEqualTo(Role("Status").Bounds.Bottom + 5);
            game.LibraryNameStyle = LibraryNameStyle.NameOnly; Settle(window); Role("Subtitle").IsVisible.Should().BeFalse();
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
