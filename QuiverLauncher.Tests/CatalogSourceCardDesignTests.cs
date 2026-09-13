using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class CatalogSourceCardDesignTests
{
    [AvaloniaTheory]
    [InlineData(320)]
    [InlineData(360)]
    [InlineData(1200)]
    public void Cards_keep_status_errors_and_actions_readable_at_supported_widths(int width)
    {
        var model = new CatalogViewModel();
        model.Sources.Add(CatalogSourceListItem.FromSource(new()
        {
            Name = "A very long catalog name for classic console ports and recreations",
            Description = "A deliberately long description that should wrap to two lines beside the square icon without displacing the actions.",
            PendingReviewCount = 12, ListAppCount = 62, LibraryAppCount = 3,
            LastFetchedUtc = new DateTime(2026, 9, 13, 9, 30, 0, DateTimeKind.Utc),
            LastError = "Unable to refresh this list (using cached copy)",
            CachedListVersion = "1.0", UpdateAvailable = true
        }));
        var view = new CatalogSourcesView { DataContext = model };
        view.Styles.Add(new StyleInclude(new Uri("avares://QuiverLauncher/"))
        {
            Source = new Uri("avares://QuiverLauncher/Themes/LauncherStyles.axaml")
        });
        if (width < 600) { view.Classes.Add("mobile"); view.ApplyMobileLayout(); }
        view.Margin = new Thickness(12);
        var window = new Window { Content = view, Width = width, Height = 720 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var card = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("catalog-source-card"));
            var cardPosition = card.TranslatePoint(default, view)!.Value;
            cardPosition.X.Should().BeGreaterThanOrEqualTo(0);
            (cardPosition.X + card.Bounds.Width).Should().BeLessThanOrEqualTo(view.Bounds.Width);
            var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToArray();
            texts.Select(t => t.Text).Should().Contain(["12 apps to review", "3 of 62 apps in your library", "Unable to refresh this list (using cached copy)"]);
            texts.Should().NotContain(t => t.Text == "Update available" || t.Text!.StartsWith("List v"));
            foreach (var control in card.GetVisualDescendants().OfType<Control>().Where(c => c is TextBlock or Button or CheckBox && c.IsEffectivelyVisible))
            {
                var position = control.TranslatePoint(default, card)!.Value;
                position.X.Should().BeGreaterThanOrEqualTo(0);
                (position.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(card.Bounds.Width + 1);
            }
            var image = card.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "CatalogArtwork");
            image.GetVisualAncestors().OfType<Border>().First().Bounds.Size.Should().Be(new Size(48, 48));
            image.Width.Should().Be(48);
            image.Height.Should().Be(48);
            image.Stretch.Should().Be(Stretch.Uniform);
            var buttons = card.GetVisualDescendants().OfType<Button>().Where(b => b is not ToggleButton).ToArray();
            buttons.Select(b => b.Content).Should().Equal("Review apps", "Remove");
            buttons[0].Focus();
            buttons[0].IsFocused.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Icon_fallback_is_visible_while_loading_and_after_failure(bool succeeds)
    {
        var previous = ImageLoader.AsyncImageLoader;
        var loader = new DelayedLoader();
        ImageLoader.AsyncImageLoader = loader;
        var model = new CatalogViewModel();
        model.Sources.Add(CatalogSourceListItem.FromSource(new() { IconUrl = "https://example.com/icon.png" }));
        var view = new CatalogSourcesView { DataContext = model };
        var window = new Window { Content = view, Width = 600, Height = 500 };
        using var bitmap = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96));
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await loader.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var fallback = view.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "CatalogIconFallback");
            var image = view.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "CatalogArtwork");
            fallback.IsVisible.Should().BeTrue();
            if (succeeds) loader.Result.SetResult(bitmap);
            else loader.Result.SetException(new IOException("Invalid image or offline server"));
            for (var attempt = 0; attempt < 30 && ImageLoader.GetIsLoading(image); attempt++)
            {
                await Task.Delay(10); Dispatcher.UIThread.RunJobs();
            }
            ImageLoader.GetIsLoading(image).Should().BeFalse();
            fallback.IsVisible.Should().Be(!succeeds);
            if (succeeds) image.Source.Should().BeSameAs(bitmap);
            else image.Source.Should().BeNull();
        }
        finally { loader.Result.TrySetResult(null); window.Close(); ImageLoader.AsyncImageLoader = previous; }
    }

    [Fact]
    public void Review_status_does_not_measure_library_completion()
    {
        var source = new AppCatalogSource { ListAppCount = 62, LibraryAppCount = 3, CachedListVersion = "1", AcknowledgedListVersion = "1" };
        var card = CatalogSourceListItem.FromSource(source);
        card.ReviewStatusText.Should().Be("All reviewed");
        card.ReviewButtonText.Should().Be("Browse apps");
        source.PendingReviewCount = 1;
        card = CatalogSourceListItem.FromSource(source);
        card.ReviewStatusText.Should().Be("1 app to review");
        card.ReviewButtonText.Should().Be("Review apps");
        source.Enabled = false;
        CatalogSourceListItem.FromSource(source).ShowReviewPendingStyle.Should().BeFalse();
    }

    [Fact]
    public void Versions_are_available_in_title_tooltip()
    {
        var source = new AppCatalogSource { Name = "Catalog", CachedListVersion = "2", AcknowledgedListVersion = "1" };
        CatalogSourceListItem.FromSource(source).TitleToolTip.Should().Contain("Catalog version: 2").And.Contain("Last reviewed version: 1");
        source.AcknowledgedListVersion = null;
        CatalogSourceListItem.FromSource(source).TitleToolTip.Should().Contain("Not reviewed yet");
    }

    private sealed class DelayedLoader : IAsyncImageLoader
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Bitmap?> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Bitmap?> ProvideImageAsync(string url) { Started.TrySetResult(); return Result.Task; }
        public void Dispose() { }
    }
}
