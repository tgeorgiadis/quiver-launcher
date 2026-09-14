using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class BackgroundImageTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Local_background_loads_on_startup_or_selection_and_can_be_replaced_and_cleared(bool savedAtStartup)
    {
        var directory = Path.Combine(Path.GetTempPath(), "quiver-background-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var firstPath = Path.Combine(directory, "background #1 café.png");
        var secondPath = Path.Combine(directory, "background #2.png");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7S8AAAAASUVORK5CYII=");
        File.WriteAllBytes(firstPath, png);
        File.WriteAllBytes(secondPath, png);
        var store = new Store(directory);
        if (savedAtStartup) store.Current.BackgroundImagePath = firstPath;
        var previousLoader = ImageLoader.AsyncImageLoader;
        var loader = new LauncherArtworkLoader(Path.Combine(directory, "cache"));
        ImageLoader.AsyncImageLoader = loader;
        var view = new MainView(new() { SettingsStore = store, EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        var host = (ISettingsFeatureHost)view;
        try
        {
            window.Show();
            var background = view.FindControl<Image>("StaticBackgroundImage")!;
            if (!savedAtStartup)
            {
                background.Source.Should().BeNull();
                store.Current.BackgroundImagePath = firstPath;
                host.NotifyPresentationChanged();
            }

            await WaitForImageAsync(background);
            background.Source.Should().BeAssignableTo<Bitmap>("a selected local file must reach the rendered image");
            background.IsVisible.Should().BeTrue();
            background.Bounds.Width.Should().BeGreaterThan(0);
            background.Opacity.Should().BeApproximately(store.Current.BackgroundOpacity, 0.001);
            var firstImage = background.Source;

            store.Current.BackgroundImagePath = secondPath;
            store.Current.BackgroundOpacity = 0.65f;
            host.NotifyPresentationChanged();
            await WaitForImageAsync(background);
            background.Source.Should().BeAssignableTo<Bitmap>().And.NotBeSameAs(firstImage);
            background.Opacity.Should().BeApproximately(0.65, 0.001);

            store.Current.BackgroundImagePath = "";
            host.NotifyPresentationChanged();
            Dispatcher.UIThread.RunJobs();
            background.Source.Should().BeNull("clearing the setting must remove the displayed image");

            store.Current.BackgroundImagePath = Path.Combine(directory, "missing.png");
            host.NotifyPresentationChanged();
            Dispatcher.UIThread.RunJobs();
            background.Source.Should().BeNull();
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            ImageLoader.AsyncImageLoader = previousLoader;
            loader.Dispose();
            Directory.Delete(directory, true);
        }
    }

    private static async Task WaitForImageAsync(Image image)
    {
        Dispatcher.UIThread.RunJobs();
        for (var attempt = 0; attempt < 100 && ImageLoader.GetIsLoading(image); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        ImageLoader.GetIsLoading(image).Should().BeFalse();
    }

    private sealed class Store(string directory) : ISettingsStore
    {
        public AppSettings Current { get; } = new()
        {
            FirstStartup = false,
            AppsPath = Path.Combine(directory, "Apps"),
            EnableGamepadInput = false
        };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
