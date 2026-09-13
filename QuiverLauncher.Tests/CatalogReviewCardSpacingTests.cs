using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class CatalogReviewCardSpacingTests
{
    [AvaloniaTheory]
    [InlineData(1280)]
    [InlineData(360)]
    [InlineData(320)]
    public void Compatibility_updates_leave_compact_cards_actions_and_focus_in_place(int width)
    {
        var view = new CatalogReviewView();
        view.Model.SettingsModel = new SettingsViewModel(new Store(width < 600 ? 128 : 180));
        view.Styles.Add(new StyleInclude(new Uri("avares://QuiverLauncher/"))
        { Source = new Uri("avares://QuiverLauncher/Themes/LauncherStyles.axaml") });
        if (width < 600) view.Classes.Add("mobile");
        var rows = Enumerable.Range(1, 8).Select(i => new CatalogSyncRowItem
        {
            IdentityKey = "spacing/" + i, Status = CatalogSyncStatus.InExternalOnly,
            External = new GameInfo { Name = "A long catalog game title " + i, Project = "Project " + i }
        }).ToArray();
        view.Model.Rows.UpdateWith(rows);
        var list = view.FindControl<ListBox>("CatalogReviewGridItemsControl")!;
        ((Panel)list.Parent!).Children.Remove(list);
        view.Content = list;
        list.IsVisible = true;
        var window = new Window { Width = width, Height = 700, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var cards = view.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("catalog-review-grid-card")).ToArray();
            cards.Length.Should().BeGreaterThan(1);
            var card = cards[0];
            var body = card.GetVisualDescendants().OfType<StackPanel>()
                .Single(p => p.Classes.Contains("catalog-review-grid-body"));
            var subtitle = body.Children.OfType<HoverScrollText>().Last();
            var actions = body.Children.OfType<ItemsControl>().Single();
            (actions.Bounds.Y - subtitle.Bounds.Bottom).Should().BeInRange(6, 10,
                "no empty compatibility line should separate the project name and actions");
            var overlay = card.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("catalog-review-compatibility"));
            overlay.IsVisible.Should().BeFalse();
            var button = actions.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible);
            button.Focus();
            list.SelectedItem = rows[0];
            var bounds = cards.Select(c => c.Bounds).ToArray();
            var buttonPosition = button.TranslatePoint(default, window);
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            var offset = scroll.Offset;
            foreach (var (state, text) in new[]
            {
                (CatalogCompatibilityState.Checking, "Checking compatibility…"),
                (CatalogCompatibilityState.Unverified, "Compatibility unverified"),
                (CatalogCompatibilityState.Unavailable, "Not available for Windows"),
                (CatalogCompatibilityState.Available, "")
            })
            {
                rows[0].SetCompatibility(state, text);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                overlay.IsVisible.Should().Be(text.Length > 0);
                if (overlay.IsVisible)
                {
                    var artwork = card.GetVisualDescendants().OfType<Border>()
                        .Single(b => b.Classes.Contains("catalog-review-icon"));
                    overlay.Bounds.Bottom.Should().BeLessThanOrEqualTo(artwork.Bounds.Bottom);
                    overlay.Background.Should().NotBeNull();
                }
                cards.Select(c => c.Bounds).Should().Equal(bounds);
                button.TranslatePoint(default, window).Should().Be(buttonPosition);
                button.IsFocused.Should().BeTrue();
                list.SelectedItem.Should().BeSameAs(rows[0]);
                scroll.Offset.Should().Be(offset);
                list.Items[0].Should().BeSameAs(rows[0]);
            }
        }
        finally { window.Close(); }
    }

    private sealed class Store(int size) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { SlotSize = size };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
